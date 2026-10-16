using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;
using SocialPublisher.API.Services;

namespace SocialPublisher.API.Controllers
{
    /// <summary>Form model for the media upload (Swagger-friendly).</summary>
    public class MediaUploadRequest
    {
        public IFormFile File { get; set; } = null!;
    }

    [ApiController]
    [Route("api/social/media")]
    public class MediaController : ControllerBase
    {
        // ── Limits ────────────────────────────────────────────────────────────────
        private const long MaxImageBytes = 8 * 1024 * 1024;  //   8 MB  – Instagram limit
        private const long MaxVideoBytes = 100 * 1024 * 1024; // 100 MB  – reasonable POC limit
        private const long MaxDocumentBytes = 50 * 1024 * 1024; //  50 MB  – LinkedIn document limit

        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public MediaController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        /// <summary>
        /// Uploads an image (JPEG / PNG, max 8 MB), a video (MP4 / MOV, max 100 MB)
        /// or a LinkedIn document (PDF, Word, PowerPoint, Excel, CSV, max 50 MB).
        /// Returns a media ID to use in POST /api/social/posts.
        /// </summary>
        [HttpPost]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(105 * 1024 * 1024)] // slightly above MaxVideoBytes
        public async Task<IActionResult> Upload([FromForm] MediaUploadRequest request, CancellationToken cancellationToken)
        {
            var file = request?.File;

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file was uploaded." });

            // ── Read the first 16 bytes to detect the real file type ─────────────
            var header = new byte[16];
            int read;
            await using (var s = file.OpenReadStream())
                read = await s.ReadAsync(header.AsMemory(0, 16), cancellationToken);

            if (read < 4)
                return BadRequest(new { error = "Unsupported file format." });

            var originalExt = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();

            // ── Detect image formats ─────────────────────────────────────────────
            var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

            // ── Detect video formats ─────────────────────────────────────────────
            // MP4 / MOV: bytes 4-7 = "ftyp"  (ISO Base Media)
            var isMp4 = read >= 8
                        && header[4] == 0x66 && header[5] == 0x74
                        && header[6] == 0x79 && header[7] == 0x70;
            // AVI: bytes 0-3 = "RIFF"
            var isAvi = header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46;

            // ── Detect document formats ──────────────────────────────────────────
            // PDF: starts with "%PDF"
            var isPdf = header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;
            // DOCX / XLSX / PPTX (ZIP-based Office Open XML): starts with PK\x03\x04
            var isZipOffice = header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
            // DOC / XLS / PPT (legacy Compound Document): starts with D0 CF 11 E0
            var isOle = header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0;

            // The file extension tells the Office formats apart (they share the same first bytes)
            string? documentExt = null;
            if (isPdf)
                documentExt = ".pdf";
            else if (isZipOffice && (originalExt == ".docx" || originalExt == ".xlsx" || originalExt == ".pptx"))
                documentExt = originalExt;
            else if (isOle && (originalExt == ".doc" || originalExt == ".xls" || originalExt == ".ppt"))
                documentExt = originalExt;
            else if (originalExt == ".csv" && LooksLikeText(header, read))
                documentExt = ".csv";

            var isDocument = documentExt != null;
            var isImage = isJpeg || isPng;
            var isVideo = isMp4 || isAvi;

            if (!isImage && !isVideo && !isDocument)
                return BadRequest(new { error = "Unsupported format. Allowed: JPG, PNG (images) | MP4, MOV (videos) | PDF, DOC, DOCX, PPT, PPTX, XLS, XLSX, CSV (LinkedIn documents)." });

            // ── Size checks ──────────────────────────────────────────────────────
            if (isImage && file.Length > MaxImageBytes)
                return BadRequest(new { error = "Image is too large. Maximum size is 8 MB." });

            if (isVideo && file.Length > MaxVideoBytes)
                return BadRequest(new { error = "Video is too large. Maximum size is 100 MB." });

            if (isDocument && file.Length > MaxDocumentBytes)
                return BadRequest(new { error = "Document is too large. Maximum size is 50 MB." });

            // ── Build file name and save ─────────────────────────────────────────
            string ext;
            string mediaType;

            if (isJpeg) { ext = ".jpg"; mediaType = "Image"; }
            else if (isPng) { ext = ".png"; mediaType = "Image"; }
            else if (isMp4) { ext = ".mp4"; mediaType = "Video"; }
            else if (isAvi) { ext = ".avi"; mediaType = "Video"; }
            else { ext = documentExt!; mediaType = "Document"; }

            var storedName = $"{Guid.NewGuid():N}{ext}";
            var folder = UploadPaths.GetFolder(_env.ContentRootPath);
            Directory.CreateDirectory(folder);

            await using (var fs = System.IO.File.Create(Path.Combine(folder, storedName)))
                await file.CopyToAsync(fs, cancellationToken);

            // ── Save metadata to DB ──────────────────────────────────────────────
            var media = new SocialPostMedia
            {
                FileName = Path.GetFileName(file.FileName) ?? file.FileName,
                FileUrl = $"{UploadPaths.RequestPath}/{storedName}",
                MediaType = mediaType
            };

            _context.SocialPostMedia.Add(media);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                id = media.Id,
                fileName = media.FileName,
                url = media.FileUrl,
                mediaType = media.MediaType
            });
        }

        /// <summary>CSV files have no signature; we only check that the start of the file is plain text (no zero bytes).</summary>
        private static bool LooksLikeText(byte[] header, int length)
        {
            for (var i = 0; i < length; i++)
                if (header[i] == 0) return false;
            return true;
        }
    }
}
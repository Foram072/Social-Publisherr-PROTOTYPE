using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    /// <summary>
    /// Publishes to LinkedIn (posting as the person who connected the account).
    ///
    ///   text only         -> ugcPosts
    ///   1 to 9 images     -> assets/registerUpload + ugcPosts
    ///   1 video           -> assets/registerUpload + ugcPosts
    ///   1 document        -> a real, swipeable LinkedIn document post (Documents API + Posts API)
    ///        PDF, DOC, DOCX, PPT, PPTX : uploaded as they are
    ///        Excel and other files     : converted to PDF with LibreOffice first
    ///        If LinkedIn refuses the native post, the pages are posted as images instead (max 9).
    ///
    /// Set SocialMedia:LinkedIn:UseNativeDocuments = "false" to always use page images.
    ///
    /// LinkedIn cannot mix media types in one post.
    /// </summary>
    public class LinkedInPublisher : ISocialPublisher
    {
        public string PlatformName => "linkedin";

        private const int MaxTextLength = 3000;
        private const int MaxImages = 9;

        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".gif" };
        private static readonly string[] VideoExts = { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
        private static readonly string[] NativeDocs = { ".pdf", ".doc", ".docx", ".ppt", ".pptx" };
        private static readonly string[] ConvertDocs = { ".xls", ".xlsx", ".xlsm", ".csv", ".ods", ".odt", ".odp", ".rtf", ".txt" };

        private readonly HttpClient _httpClient;
        private readonly ILogger<LinkedInPublisher> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public LinkedInPublisher(HttpClient httpClient, ILogger<LinkedInPublisher> logger, IWebHostEnvironment env, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _env = env;
            _configuration = configuration;
        }

        // Only used for the optional native-document mode (/rest/ APIs need a YYYYMM version header).
        private string ApiVersion => _configuration["SocialMedia:LinkedIn:ApiVersion"] ?? "202608";

        // Native (swipeable) document posts are ON by default. Set "UseNativeDocuments": "false" to always use page images.
        private bool UseNativeDocuments =>
            !string.Equals(_configuration["SocialMedia:LinkedIn:UseNativeDocuments"], "false", StringComparison.OrdinalIgnoreCase);

        public async Task<PublishResultDto> PublishAsync(SocialPost post, SocialAccount account, CancellationToken cancellationToken)
        {
            var result = new PublishResultDto { Platform = PlatformName };
            var tempFiles = new List<string>();
            string? tempDir = null;

            try
            {
                // ── Validation ────────────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(account.ExternalAccountId) || string.IsNullOrWhiteSpace(account.AccessToken))
                    return Fail(result, "LinkedIn is not connected yet. Open http://localhost:5293/api/social/accounts/connect/linkedin in a browser tab and approve.");

                if (string.IsNullOrWhiteSpace(post.Content) || post.Content.Length > MaxTextLength)
                    return Fail(result, $"LinkedIn post text must be between 1 and {MaxTextLength} characters.");

                var token = account.AccessToken;
                var author = await ResolveAuthorAsync(account.ExternalAccountId, token, cancellationToken);

                // ── Sort the selected files by kind (keeping the user's order) ─
                // Ignore accidental duplicates of the same media row, then keep the user's order
                var items = (post.Media ?? new List<SocialPostMedia>())
                                    .GroupBy(m => m.Id)
                                    .Select(g => g.First())
                                    .OrderBy(m => m.Id)
                                    .ToList();
                _logger.LogInformation("LinkedIn post media: {Media}", string.Join(", ", items.Select(m => $"{m.Id}:{m.FileName}")));
                var images = items.Where(m => KindOf(m) == "Image").ToList();
                var videos = items.Where(m => KindOf(m) == "Video").ToList();
                var documents = items.Where(m => KindOf(m) == "Document")
                                     .GroupBy(m => (m.FileName ?? m.FileUrl ?? string.Empty).ToLowerInvariant())
                                     .Select(g => g.First())
                                     .ToList();

                if (documents.Count > 1)
                    return Fail(result, $"LinkedIn allows only one document per post. This post contains {documents.Count}: " +
                                        string.Join(", ", documents.Select(d => d.FileName)) + ". Remove the extra documents.");

                if (documents.Count == 1 && (images.Count > 0 || videos.Count > 0))
                    return Fail(result, "LinkedIn cannot combine a document with images or a video. Post the document on its own.");

                if (videos.Count > 1)
                    return Fail(result, "LinkedIn allows only one video per post. Remove the extra videos.");

                if (videos.Count == 1 && images.Count > 0)
                    return Fail(result, "LinkedIn cannot combine images and a video in one post. Use only images, or a single video.");

                if (images.Count > MaxImages)
                    return Fail(result, $"LinkedIn allows up to {MaxImages} images in one post. You selected {images.Count}.");

                // ── Document: try the real LinkedIn document post first ───────
                // If LinkedIn refuses it (for example the app has no access to the Documents API),
                // we fall back to posting the pages as images so the post still goes out.
                if (documents.Count == 1 && UseNativeDocuments)
                {
                    try
                    {
                        var nativeResult = await PublishNativeDocumentAsync(
                            new PublishResultDto { Platform = PlatformName }, post, author, token, documents[0], cancellationToken);

                        if (nativeResult.Status == "Published")
                            return nativeResult;

                        _logger.LogWarning("Native LinkedIn document post failed ({Error}). Falling back to page images.", nativeResult.Error);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Native LinkedIn document post failed. Falling back to page images.");
                    }
                }

                // ── Collect the files to upload ───────────────────────────────
                var filesToUpload = new List<string>();
                var mediaCategory = "NONE";

                if (documents.Count == 1)
                {
                    _logger.LogInformation("Turning the document into page images for LinkedIn...");
                    var (pageImages, dir) = await DocumentToImagesAsync(documents[0], cancellationToken);
                    tempFiles.AddRange(pageImages);
                    tempDir = dir;
                    filesToUpload.AddRange(pageImages);
                    mediaCategory = "IMAGE";
                }
                else if (videos.Count == 1)
                {
                    filesToUpload.Add(ResolveFile(videos[0], "video"));
                    mediaCategory = "VIDEO";
                }
                else if (images.Count > 0)
                {
                    foreach (var image in images)
                        filesToUpload.Add(ResolveFile(image, "image"));
                    mediaCategory = "IMAGE";
                }

                // ── Upload each file (registerUpload + PUT) ───────────────────
                var assetUrns = new List<string>();
                foreach (var file in filesToUpload)
                    assetUrns.Add(await UploadAssetAsync(author, token, file, isVideo: mediaCategory == "VIDEO", cancellationToken));

                // ── Create the post (ugcPosts) ────────────────────────────────
                var shareContent = new Dictionary<string, object>
                {
                    ["shareCommentary"] = new { text = post.Content },
                    ["shareMediaCategory"] = mediaCategory
                };

                if (assetUrns.Count > 0)
                {
                    shareContent["media"] = assetUrns.Select(urn => new Dictionary<string, object>
                    {
                        ["status"] = "READY",
                        ["media"] = urn
                    }).ToArray();
                }

                var payload = new Dictionary<string, object>
                {
                    ["author"] = author,
                    ["lifecycleState"] = "PUBLISHED",
                    ["specificContent"] = new Dictionary<string, object>
                    {
                        ["com.linkedin.ugc.ShareContent"] = shareContent
                    },
                    ["visibility"] = new Dictionary<string, object>
                    {
                        ["com.linkedin.ugc.MemberNetworkVisibility"] = "PUBLIC"
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/v2/ugcPosts");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
                request.Content = JsonContent.Create(payload);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("LinkedIn ugcPosts ({Status}): {Body}", (int)response.StatusCode, responseBody);

                if (!response.IsSuccessStatusCode)
                    return Fail(result, WithHint(response.StatusCode, ExtractError(response.StatusCode, responseBody)));

                var externalId = ReadString(responseBody, "id");
                if (string.IsNullOrEmpty(externalId) && response.Headers.TryGetValues("x-restli-id", out var hdr))
                    externalId = hdr.FirstOrDefault();

                result.Status = "Published";
                result.ExternalPostId = externalId;
                if (!string.IsNullOrEmpty(externalId))
                    result.PostUrl = $"https://www.linkedin.com/feed/update/{externalId}";

                return result;
            }
            catch (OperationCanceledException)
            {
                return Fail(result, "The LinkedIn request timed out or was cancelled. Check LinkedIn before retrying to avoid a duplicate post.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LinkedIn publishing failed");
                return Fail(result, ex.Message);
            }
            finally
            {
                foreach (var tmp in tempFiles)
                    try { File.Delete(tmp); } catch { /* ignore */ }
                if (tempDir != null)
                    try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Who is posting? (the author URN)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Asks LinkedIn who owns the access token and uses that person. This avoids problems when the
        /// saved account id is a company number (such as CompanyId in appsettings.json) instead of the
        /// member id. A full URN saved in the account (urn:li:organization:123) is used as it is.
        /// </summary>
        private async Task<string> ResolveAuthorAsync(string storedId, string token, CancellationToken ct)
        {
            storedId = storedId.Trim();

            if (storedId.StartsWith("urn:li:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("LinkedIn author (from saved account): {Author}", storedId);
                return storedId;
            }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await _httpClient.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if (resp.IsSuccessStatusCode)
                {
                    var sub = ReadString(body, "sub");
                    if (!string.IsNullOrWhiteSpace(sub))
                    {
                        var author = $"urn:li:person:{sub}";
                        if (!string.Equals(sub, storedId, StringComparison.Ordinal))
                            _logger.LogWarning("Saved LinkedIn id '{Stored}' differs from the token owner '{Sub}'. Using the token owner.", storedId, sub);
                        _logger.LogInformation("LinkedIn author (from token): {Author}", author);
                        return author;
                    }
                }
                else
                {
                    _logger.LogWarning("LinkedIn userinfo ({Status}): {Body}", (int)resp.StatusCode, body);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the LinkedIn profile; using the saved account id.");
            }

            var fallback = $"urn:li:person:{storedId}";
            _logger.LogInformation("LinkedIn author (from saved account id): {Author}", fallback);
            return fallback;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  File kind detection
        // ══════════════════════════════════════════════════════════════════════════

        private static string FileExt(SocialPostMedia m)
        {
            var name = !string.IsNullOrWhiteSpace(m.FileName) ? m.FileName! : (m.FileUrl ?? string.Empty);
            try { return Path.GetExtension(name.Split('?')[0]).ToLowerInvariant(); }
            catch { return string.Empty; }
        }

        private static string KindOf(SocialPostMedia m)
        {
            var ext = FileExt(m);
            if (ImageExts.Contains(ext)) return "Image";
            if (VideoExts.Contains(ext)) return "Video";
            if (NativeDocs.Contains(ext) || ConvertDocs.Contains(ext)) return "Document";
            return m.MediaType ?? string.Empty;   // fall back to what the app saved
        }

        private string ResolveFile(SocialPostMedia media, string label)
        {
            var filePath = UploadPaths.GetFilePath(_env.ContentRootPath, media.FileUrl);
            if (!File.Exists(filePath))
                throw new Exception($"The uploaded {label} '{media.FileName}' was not found on the server. Please upload it again.");
            return filePath;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Image / video upload  (v2/assets registerUpload, then PUT the bytes)
        // ══════════════════════════════════════════════════════════════════════════
        private async Task<string> UploadAssetAsync(string owner, string token, string filePath, bool isVideo, CancellationToken ct)
        {
            var recipe = isVideo ? "urn:li:digitalmediaRecipe:feedshare-video" : "urn:li:digitalmediaRecipe:feedshare-image";

            var registerRequest = new Dictionary<string, object>
            {
                ["recipes"] = new[] { recipe },
                ["owner"] = owner,
                ["serviceRelationships"] = new[]
                {
                    new { relationshipType = "OWNER", identifier = "urn:li:userGeneratedContent" }
                }
            };
            if (isVideo)
                registerRequest["supportedUploadMechanism"] = new[] { "SINGLE_REQUEST_UPLOAD" };

            using var registerReq = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/v2/assets?action=registerUpload");
            registerReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            registerReq.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            registerReq.Content = JsonContent.Create(new { registerUploadRequest = registerRequest });

            using var registerResp = await _httpClient.SendAsync(registerReq, ct);
            var registerJson = await registerResp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("LinkedIn registerUpload {Kind} ({Status}): {Body}", isVideo ? "video" : "image", (int)registerResp.StatusCode, registerJson);

            if (!registerResp.IsSuccessStatusCode)
                throw new Exception(WithHint(registerResp.StatusCode, ExtractError(registerResp.StatusCode, registerJson)));

            using var registerDoc = JsonDocument.Parse(registerJson);
            var value = registerDoc.RootElement.GetProperty("value");
            var asset = value.GetProperty("asset").GetString()!;
            var uploadUrl = value.GetProperty("uploadMechanism")
                                 .GetProperty("com.linkedin.digitalmedia.uploading.MediaUploadHttpRequest")
                                 .GetProperty("uploadUrl").GetString()!;

            await PutFileBytesAsync(uploadUrl, token, filePath, ct);
            return asset;
        }

        private async Task PutFileBytesAsync(string uploadUrl, string accessToken, string filePath, CancellationToken ct)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);

            using var uploadReq = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            uploadReq.Content = new ByteArrayContent(bytes);
            uploadReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var uploadResp = await _httpClient.SendAsync(uploadReq, ct);
            if (!uploadResp.IsSuccessStatusCode)
            {
                var uploadBody = await uploadResp.Content.ReadAsStringAsync(ct);
                throw new Exception($"LinkedIn media upload failed ({(int)uploadResp.StatusCode}): {uploadBody}".Trim());
            }

            _logger.LogInformation("LinkedIn media bytes uploaded successfully");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Document -> page images  (PDF directly; Word/PowerPoint/Excel via LibreOffice)
        // ══════════════════════════════════════════════════════════════════════════
        private async Task<(List<string> Files, string Dir)> DocumentToImagesAsync(SocialPostMedia document, CancellationToken ct)
        {
            var ext = FileExt(document);
            var filePath = ResolveFile(document, "document");

            string? convertedPdf = null;
            var outDir = Path.Combine(Path.GetTempPath(), "li_pages_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            var outputs = new List<string>();

            try
            {
                var pdfPath = filePath;
                if (ext != ".pdf")
                {
                    _logger.LogInformation("Converting {Ext} to PDF...", ext);
                    convertedPdf = await ConvertDocumentToPdfAsync(filePath, ext, ct);
                    pdfPath = convertedPdf;
                }

                var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
                int pages;
                try
                {
                    pages = Conversion.GetPageCount(bytes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not read the PDF");
                    throw new Exception("Could not read the PDF. Please make sure it is not corrupted or password-protected.");
                }

                if (pages == 0)
                    throw new Exception("The document has no pages.");

                if (pages > MaxImages)
                    _logger.LogWarning("Document has {Pages} pages; only the first {Max} are posted (LinkedIn image limit).", pages, MaxImages);

                var pagesToConvert = Math.Min(pages, MaxImages);
                var options = new RenderOptions { Dpi = 200 };

                for (var i = 0; i < pagesToConvert; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var pagePath = Path.Combine(outDir, $"page_{i + 1:00}.png");
                    Conversion.SavePng(pagePath, bytes, page: i, options: options);
                    outputs.Add(pagePath);
                }

                return (outputs, outDir);
            }
            catch
            {
                try { Directory.Delete(outDir, true); } catch { }
                throw;
            }
            finally
            {
                if (convertedPdf != null)
                    try { Directory.Delete(Path.GetDirectoryName(convertedPdf)!, true); } catch { }
            }
        }

        /// <summary>
        /// Spreadsheets (xls, xlsx, csv) are converted by our own code (no LibreOffice needed).
        /// Anything else that is not natively supported falls back to LibreOffice.
        /// </summary>
        private async Task<string> ConvertDocumentToPdfAsync(string sourcePath, string ext, CancellationToken ct)
        {
            if (ExcelToPdfConverter.CanConvert(ext))
                return await Task.Run(() => ExcelToPdfConverter.Convert(sourcePath), ct);

            return await ConvertToPdfAsync(sourcePath, ct);
        }

        /// <summary>
        /// Converts a file (docx, pptx, xlsx, csv, ...) to PDF using LibreOffice (headless).
        /// Install LibreOffice on the server, or set SocialMedia:LibreOfficePath in appsettings.json.
        /// </summary>
        private async Task<string> ConvertToPdfAsync(string sourcePath, CancellationToken ct)
        {
            var soffice = _configuration["SocialMedia:LibreOfficePath"];
            if (string.IsNullOrWhiteSpace(soffice))
            {
                var windowsDefault = @"C:\Program Files\LibreOffice\program\soffice.exe";
                soffice = File.Exists(windowsDefault) ? windowsDefault : "soffice";
            }

            var outDir = Path.Combine(Path.GetTempPath(), "li_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);

            var psi = new ProcessStartInfo
            {
                FileName = soffice,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add(sourcePath);

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                try { Directory.Delete(outDir, true); } catch { }
                _logger.LogError(ex, "Could not start LibreOffice");
                throw new Exception(
                    "This file type must be converted to PDF before posting to LinkedIn, but LibreOffice was not found on the server. " +
                    "Install LibreOffice (or set SocialMedia:LibreOfficePath), or export the file as a PDF yourself and upload the PDF.");
            }

            if (proc == null)
                throw new Exception("Could not start the PDF converter (LibreOffice).");

            using (proc)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(90));

                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(true); } catch { }
                    try { Directory.Delete(outDir, true); } catch { }
                    throw new Exception("Converting the file to PDF took too long. Please upload it as a PDF instead.");
                }
            }

            var pdf = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sourcePath) + ".pdf");
            if (!File.Exists(pdf))
            {
                try { Directory.Delete(outDir, true); } catch { }
                throw new Exception("Could not convert the file to PDF. Please open it, export it as a PDF, and upload the PDF.");
            }

            return pdf;
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  OPTIONAL native document post (only when UseNativeDocuments = true)
        //    /rest/documents initializeUpload -> PUT bytes -> wait -> /rest/posts
        // ══════════════════════════════════════════════════════════════════════════
        private async Task<PublishResultDto> PublishNativeDocumentAsync(
            PublishResultDto result, SocialPost post, string author, string token, SocialPostMedia document, CancellationToken ct)
        {
            var ext = FileExt(document);
            var filePath = ResolveFile(document, "document");

            // The title LinkedIn shows on the document is the ORIGINAL file name including its extension
            // (for example "Presentation.pptx", "sample-budget.xlsx"), even when we had to convert it to PDF.
            var title = Path.GetFileName(
                            string.IsNullOrWhiteSpace(document.FileName) ? filePath : document.FileName!);
            if (string.IsNullOrWhiteSpace(title)) title = "Document";
            if (title.Length > 300) title = title.Substring(0, 300);

            string? converted = null;
            try
            {
                // LinkedIn documents accept PDF, DOC, DOCX, PPT, PPTX. Excel and others are converted to PDF first.
                if (!NativeDocs.Contains(ext))
                {
                    converted = await ConvertDocumentToPdfAsync(filePath, ext, ct);
                    filePath = converted;
                }

                return await PublishNativeDocumentCoreAsync(result, post, author, token, filePath, title, ct);
            }
            finally
            {
                if (converted != null)
                    try { Directory.Delete(Path.GetDirectoryName(converted)!, true); } catch { /* ignore */ }
            }
        }

        private async Task<PublishResultDto> PublishNativeDocumentCoreAsync(
            PublishResultDto result, SocialPost post, string author, string token, string filePath, string title, CancellationToken ct)
        {
            using var initReq = NewRestRequest(HttpMethod.Post, "https://api.linkedin.com/rest/documents?action=initializeUpload", token);
            initReq.Content = JsonContent.Create(new { initializeUploadRequest = new { owner = author } });

            using var initResp = await _httpClient.SendAsync(initReq, ct);
            var initJson = await initResp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("LinkedIn documents initializeUpload ({Status}): {Body}", (int)initResp.StatusCode, initJson);

            if (!initResp.IsSuccessStatusCode)
                return Fail(result, WithHint(initResp.StatusCode, ExtractError(initResp.StatusCode, initJson)));

            using var initDoc = JsonDocument.Parse(initJson);
            var value = initDoc.RootElement.GetProperty("value");
            var uploadUrl = value.GetProperty("uploadUrl").GetString()!;
            var documentUrn = value.GetProperty("document").GetString()!;

            await PutFileBytesAsync(uploadUrl, token, filePath, ct);
            await WaitForDocumentAsync(documentUrn, token, ct);

            var postBody = new Dictionary<string, object>
            {
                ["author"] = author,
                ["commentary"] = ToLittleText(post.Content),
                ["visibility"] = "PUBLIC",
                ["distribution"] = new Dictionary<string, object>
                {
                    ["feedDistribution"] = "MAIN_FEED",
                    ["targetEntities"] = Array.Empty<object>(),
                    ["thirdPartyDistributionChannels"] = Array.Empty<object>()
                },
                ["content"] = new Dictionary<string, object>
                {
                    ["media"] = new Dictionary<string, object> { ["title"] = title, ["id"] = documentUrn }
                },
                ["lifecycleState"] = "PUBLISHED",
                ["isReshareDisabledByAuthor"] = false
            };

            using var postReq = NewRestRequest(HttpMethod.Post, "https://api.linkedin.com/rest/posts", token);
            postReq.Content = JsonContent.Create(postBody);

            using var postResp = await _httpClient.SendAsync(postReq, ct);
            var postJson = await postResp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("LinkedIn create document post ({Status}): {Body}", (int)postResp.StatusCode, postJson);

            if (!postResp.IsSuccessStatusCode)
                return Fail(result, WithHint(postResp.StatusCode, ExtractError(postResp.StatusCode, postJson)));

            string? postId = null;
            if (postResp.Headers.TryGetValues("x-restli-id", out var ids))
                postId = ids.FirstOrDefault();

            result.Status = "Published";
            result.ExternalPostId = postId;
            if (!string.IsNullOrEmpty(postId))
                result.PostUrl = $"https://www.linkedin.com/feed/update/{postId}";
            return result;
        }

        private async Task WaitForDocumentAsync(string documentUrn, string token, CancellationToken ct)
        {
            var url = $"https://api.linkedin.com/rest/documents/{Uri.EscapeDataString(documentUrn)}";

            for (var attempt = 0; attempt < 15; attempt++)
            {
                using var req = NewRestRequest(HttpMethod.Get, url, token);
                using var resp = await _httpClient.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("LinkedIn document status ({Status}): {Body}", (int)resp.StatusCode, body);

                if (!resp.IsSuccessStatusCode) return;

                var status = ReadString(body, "status");
                if (status == null || status == "AVAILABLE") return;

                if (status == "PROCESSING_FAILED")
                    throw new Exception("LinkedIn could not process the document. Check that it opens normally and has no password.");

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        private HttpRequestMessage NewRestRequest(HttpMethod method, string url, string accessToken)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            req.Headers.Add("Linkedin-Version", ApiVersion);
            return req;
        }

        // ── Text helpers (only for the native /rest/posts mode) ───────────────────

        private static string ToLittleText(string text)
        {
            var sb = new StringBuilder();
            var last = 0;

            foreach (Match m in Regex.Matches(text, @"(?<![\w&])#(\w+)"))
            {
                sb.Append(EscapeLittle(text.Substring(last, m.Index - last)));
                sb.Append("{hashtag|\\#|").Append(EscapeLittle(m.Groups[1].Value)).Append('}');
                last = m.Index + m.Length;
            }

            sb.Append(EscapeLittle(text.Substring(last)));
            return sb.ToString();
        }

        private static string EscapeLittle(string s)
        {
            const string reserved = "\\|{}@[]()<>#*_~";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                if (reserved.IndexOf(ch) >= 0) sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // ── Error helpers ─────────────────────────────────────────────────────────

        private static PublishResultDto Fail(PublishResultDto result, string message)
        {
            result.Status = "Failed";
            result.Error = message;
            return result;
        }

        private static string WithHint(HttpStatusCode status, string message)
        {
            switch ((int)status)
            {
                case 401:
                    return message + " (Your LinkedIn token has expired or is not valid. Reconnect with /api/social/accounts/connect/linkedin.)";
                case 403:
                    return message + " (LinkedIn refused permission. Check that your LinkedIn app has the 'Share on LinkedIn' product and that you reconnected after adding it.)";
                case 426:
                    return message + " (LinkedIn does not accept this API version. Set SocialMedia:LinkedIn:ApiVersion in appsettings.json to a recent month such as 202609.)";
                default:
                    return message;
            }
        }

        private static string? ReadString(string json, string property)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty(property, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            catch (JsonException) { }
            return null;
        }

        // Always shows the HTTP status and what LinkedIn really said (even if the body has no "message").
        private static string ExtractError(HttpStatusCode status, string json)
        {
            string detail = string.IsNullOrWhiteSpace(json) ? "(empty response)" : json;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("message", out var m) && !string.IsNullOrWhiteSpace(m.GetString()))
                    detail = m.GetString()!;
            }
            catch (JsonException) { }
            return $"LinkedIn API Error {(int)status}: {detail}";
        }
    }
}
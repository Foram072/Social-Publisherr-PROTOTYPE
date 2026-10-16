using System.IO;

namespace SocialPublisher.API.Services
{
    public static class UploadPaths
    {
        public const string RequestPath = "/uploads";

        public static string GetFolder(string contentRootPath) =>
            Path.Combine(contentRootPath, "wwwroot", "uploads");

        public static string GetFilePath(string contentRootPath, string fileUrl) =>
            Path.Combine(GetFolder(contentRootPath), Path.GetFileName(fileUrl));
    }
}
using QuickCull.Models;
using QuickCull.Core.Services.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XmpCore;
using XmpCore.Impl;
using XmpCore.Options;

namespace QuickCull.Core.Services.XMP
{
    public class XmpService : IXmpService
    {
        private const string QuickCullNamespace = "http://quickcull.com/xmp/1.0/";
        private const string QuickCullPrefix = "qc";

        // Standard XMP namespaces
        private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
        private const string XmpDynamicMediaNamespace = "http://ns.adobe.com/xmp/1.0/DynamicMedia/";

        static XmpService()
        {
            // Register our custom namespace
            XmpMetaFactory.SchemaRegistry.RegisterNamespace(QuickCullNamespace, QuickCullPrefix);
        }

        public async Task WriteAnalysisToXmpAsync(string imagePath, AnalysisResult analysisResult)
        {
            var xmpPath = GetXmpPath(imagePath);
            var xmpMeta = await LoadOrCreateXmpAsync(xmpPath);

            // Write analysis data to custom namespace
            xmpMeta.SetProperty(QuickCullNamespace, "AnalyzedAt", analysisResult.AnalyzedAt.ToString("O"));
            xmpMeta.SetProperty(QuickCullNamespace, "ModelVersion", analysisResult.ModelVersion);
            xmpMeta.SetProperty(QuickCullNamespace, "AnalysisVersion", analysisResult.AnalysisVersion);
            xmpMeta.SetPropertyDouble(QuickCullNamespace, "SharpnessOverall", analysisResult.SharpnessOverall);
            xmpMeta.SetPropertyDouble(QuickCullNamespace, "SharpnessSubject", analysisResult.SharpnessSubject);
            xmpMeta.SetPropertyInteger(QuickCullNamespace, "SubjectCount", analysisResult.SubjectCount);
            xmpMeta.SetPropertyDouble(QuickCullNamespace, "SubjectSharpnessPercentage", analysisResult.SubjectSharpnessPercentage);
            xmpMeta.SetPropertyBoolean(QuickCullNamespace, "EyesOpen", analysisResult.EyesOpen);
            xmpMeta.SetPropertyDouble(QuickCullNamespace, "EyeConfidence", analysisResult.EyeConfidence);
            xmpMeta.SetPropertyInteger(QuickCullNamespace, "PredictedRating", analysisResult.PredictedRating);
            xmpMeta.SetPropertyDouble(QuickCullNamespace, "PredictionConfidence", analysisResult.PredictionConfidence);
            xmpMeta.SetPropertyInteger(QuickCullNamespace, "GroupID", analysisResult.GroupID);

            // Handle subject types array
            if (analysisResult.SubjectTypes?.Any() == true)
            {
                xmpMeta.DeleteProperty(QuickCullNamespace, "SubjectTypes");
                for (int i = 0; i < analysisResult.SubjectTypes.Count; i++)
                {
                    xmpMeta.AppendArrayItem(QuickCullNamespace, "SubjectTypes",
                        new PropertyOptions { IsArray = true },
                        analysisResult.SubjectTypes[i], null);
                }
            }

            // Handle extended data
            if (analysisResult.ExtendedData?.Any() == true)
            {
                foreach (var kvp in analysisResult.ExtendedData)
                {
                    var propertyName = $"ExtendedData_{kvp.Key}";
                    xmpMeta.SetProperty(QuickCullNamespace, propertyName, kvp.Value?.ToString() ?? "");
                }
            }

            // Update modification date
            xmpMeta.SetProperty(XmpNamespace, "MetadataDate", DateTime.Now.ToString("O"));

            await SaveXmpAsync(xmpPath, xmpMeta);
        }

        public async Task WritePickStatusToXmpAsync(string imagePath, bool? pickStatus)
        {
            var xmpPath = GetXmpPath(imagePath);
            var xmpMeta = await LoadOrCreateXmpAsync(xmpPath);

            // Write pick status using Lightroom-compatible format
            if (pickStatus.HasValue)
            {
                // Use xmpDM:good for pick status (Lightroom standard)
                xmpMeta.SetPropertyBoolean(XmpDynamicMediaNamespace, "good", pickStatus.Value);

                // Also set the standard XMP rating if picked
                if (pickStatus.Value)
                {
                    // Set rating to 1 if picked, or leave existing rating if already set and > 0
                    var currentRating = GetRatingFromXmp(xmpMeta);
                    if (currentRating == 0)
                    {
                        xmpMeta.SetPropertyInteger(XmpNamespace, "Rating", 1);
                    }
                }
            }
            else
            {
                // Remove pick status
                xmpMeta.DeleteProperty(XmpDynamicMediaNamespace, "good");
            }

            // Update modification date
            xmpMeta.SetProperty(XmpNamespace, "MetadataDate", DateTime.Now.ToString("O"));

            await SaveXmpAsync(xmpPath, xmpMeta);
        }

        public async Task<AnalysisResult> ReadAnalysisFromXmpAsync(string imagePath)
        {
            var xmpPath = GetXmpPath(imagePath);
            if (!File.Exists(xmpPath))
                return null;

            var xmpMeta = await LoadXmpAsync(xmpPath);
            if (xmpMeta == null)
                return null;

            try
            {
                var result = new AnalysisResult
                {
                    Filename = Path.GetFileName(imagePath),
                    FilePath = imagePath
                };

                // Read analysis data
                if (TryGetProperty(xmpMeta, QuickCullNamespace, "AnalyzedAt", out string analyzedAtStr) &&
                    DateTime.TryParse(analyzedAtStr, out DateTime analyzedAt))
                {
                    result.AnalyzedAt = analyzedAt;
                }

                result.ModelVersion = GetPropertySafe(xmpMeta, QuickCullNamespace, "ModelVersion");
                result.AnalysisVersion = GetPropertySafe(xmpMeta, QuickCullNamespace, "AnalysisVersion");

                if (TryGetPropertyDouble(xmpMeta, QuickCullNamespace, "SharpnessOverall", out double sharpnessOverall))
                    result.SharpnessOverall = sharpnessOverall;

                if (TryGetPropertyDouble(xmpMeta, QuickCullNamespace, "SharpnessSubject", out double sharpnessSubject))
                    result.SharpnessSubject = sharpnessSubject;

                if (TryGetPropertyInt(xmpMeta, QuickCullNamespace, "SubjectCount", out int subjectCount))
                    result.SubjectCount = subjectCount;

                if (TryGetPropertyDouble(xmpMeta, QuickCullNamespace, "SubjectSharpnessPercentage", out double subjectSharpnessPercentage))
                    result.SubjectSharpnessPercentage = subjectSharpnessPercentage;

                if (TryGetPropertyBool(xmpMeta, QuickCullNamespace, "EyesOpen", out bool eyesOpen))
                    result.EyesOpen = eyesOpen;

                if (TryGetPropertyDouble(xmpMeta, QuickCullNamespace, "EyeConfidence", out double eyeConfidence))
                    result.EyeConfidence = eyeConfidence;

                if (TryGetPropertyInt(xmpMeta, QuickCullNamespace, "PredictedRating", out int predictedRating))
                    result.PredictedRating = predictedRating;

                if (TryGetPropertyDouble(xmpMeta, QuickCullNamespace, "PredictionConfidence", out double predictionConfidence))
                    result.PredictionConfidence = predictionConfidence;

                if (TryGetPropertyInt(xmpMeta, QuickCullNamespace, "GroupID", out int groupId))
                    result.GroupID = groupId;

                // Read subject types array
                result.SubjectTypes = ReadArrayProperty(xmpMeta, QuickCullNamespace, "SubjectTypes");

                // Read extended data
                result.ExtendedData = ReadExtendedData(xmpMeta);

                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<XmpMetadata> ReadAllXmpDataAsync(string imagePath)
        {
            var xmpPath = GetXmpPath(imagePath);
            if (!File.Exists(xmpPath))
                return new XmpMetadata();

            var xmpMeta = await LoadXmpAsync(xmpPath);
            if (xmpMeta == null)
                return new XmpMetadata();

            var metadata = new XmpMetadata();

            // Read last modified date
            if (TryGetProperty(xmpMeta, XmpNamespace, "MetadataDate", out string modifiedDateStr) &&
                DateTime.TryParse(modifiedDateStr, out DateTime modifiedDate))
            {
                metadata.LastModified = modifiedDate;
            }
            else
            {
                metadata.LastModified = File.GetLastWriteTime(xmpPath);
            }

            // Read analysis data
            metadata.AnalysisData = await ReadAnalysisFromXmpAsync(imagePath);

            // Read Lightroom-specific data
            metadata.LightroomRating = GetNullableRating(xmpMeta);
            metadata.LightroomPick = GetPickStatus(xmpMeta);
            metadata.LightroomLabel = GetPropertySafe(xmpMeta, XmpNamespace, "Label");

            // Check for Camera Raw adjustments
            metadata.HasCameraRawAdjustments = HasCameraRawSettings(xmpMeta);
            metadata.CameraProfile = GetPropertySafe(xmpMeta, "http://ns.adobe.com/camera-raw-settings/1.0/", "CameraProfile");

            return metadata;
        }

        public bool XmpFileExists(string imagePath)
        {
            var xmpPath = GetXmpPath(imagePath);
            return File.Exists(xmpPath);
        }

        public async Task<DateTime?> GetXmpModifiedDateAsync(string imagePath)
        {
            var xmpPath = GetXmpPath(imagePath);
            if (!File.Exists(xmpPath))
                return null;

            try
            {
                var xmpMeta = await LoadXmpAsync(xmpPath);
                if (xmpMeta != null && TryGetProperty(xmpMeta, XmpNamespace, "MetadataDate", out string dateStr) &&
                    DateTime.TryParse(dateStr, out DateTime date))
                {
                    return date;
                }
            }
            catch
            {
                // Fall back to file system date
            }

            return File.GetLastWriteTime(xmpPath);
        }

        // Helper methods
        private string GetXmpPath(string imagePath)
        {
            return Path.ChangeExtension(imagePath, ".xmp");
        }

        private async Task<IXmpMeta> LoadOrCreateXmpAsync(string xmpPath)
        {
            if (File.Exists(xmpPath))
            {
                var existingXmp = await LoadXmpAsync(xmpPath);
                if (existingXmp != null)
                    return existingXmp;
            }

            // Create new XMP with minimal Lightroom-compatible structure
            var xmpMeta = XmpMetaFactory.Create();

            // Set basic XMP toolkit identifier (Lightroom compatible)
            xmpMeta.SetProperty(XmpNamespace, "CreatorTool", "QuickCull");
            xmpMeta.SetProperty(XmpNamespace, "MetadataDate", DateTime.Now.ToString("O"));

            return xmpMeta;
        }

        private async Task<IXmpMeta> LoadXmpAsync(string xmpPath)
        {
            try
            {
                var xmpContent = await File.ReadAllTextAsync(xmpPath, Encoding.UTF8);

                // Clean up the XMP content to fix common parsing issues
                xmpContent = CleanXmpContent(xmpContent);

                var parseOptions = new ParseOptions
                {
                    RequireXmpMeta = true,
                    StrictAliasing = false,
                    FixControlChars = true
                };

                return XmpMetaFactory.ParseFromString(xmpContent, parseOptions);
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                Console.WriteLine($"Error loading XMP from {xmpPath}: {ex}");
                return null;
            }
        }

        private string CleanXmpContent(string xmpContent)
        {
            if (string.IsNullOrEmpty(xmpContent))
                return xmpContent;

            // Remove BOM if present
            if (xmpContent.StartsWith("\uFEFF"))
                xmpContent = xmpContent.Substring(1);

            // Fix the xpacket begin declaration - remove any BOMs in the begin attribute
            xmpContent = xmpContent.Replace("<?xpacket begin=\"﻿\"", "<?xpacket begin=\"\"");
            xmpContent = xmpContent.Replace("<?xpacket begin='﻿'", "<?xpacket begin=''");

            // Ensure proper xpacket structure
            if (xmpContent.Contains("<?xpacket begin=") && !xmpContent.Contains("<?xpacket end="))
            {
                // Find the end of x:xmpmeta
                var xmpmetaEndIndex = xmpContent.LastIndexOf("</x:xmpmeta>");
                if (xmpmetaEndIndex > 0)
                {
                    var beforeEnd = xmpContent.Substring(0, xmpmetaEndIndex + "</x:xmpmeta>".Length);
                    xmpContent = beforeEnd + "\n<?xpacket end=\"w\"?>";
                }
            }

            // Remove excessive whitespace after the closing tag
            var endPacketIndex = xmpContent.LastIndexOf("<?xpacket end=");
            if (endPacketIndex > 0)
            {
                var endIndex = xmpContent.IndexOf("?>", endPacketIndex);
                if (endIndex > 0)
                {
                    xmpContent = xmpContent.Substring(0, endIndex + 2);
                }
            }

            return xmpContent;
        }

        private async Task SaveXmpAsync(string xmpPath, IXmpMeta xmpMeta)
        {
            var options = new SerializeOptions
            {
                Indent = " ",
                Newline = "\n",
                UseCanonicalFormat = false,
                UseCompactFormat = false,
                IncludeThumbnailPad = false,
                ExactPacketLength = false
            };

            var xmpString = XmpMetaFactory.SerializeToString(xmpMeta, options);

            // Ensure the XMP has proper packet wrapper
            if (!xmpString.Contains("<?xpacket begin="))
            {
                xmpString = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
                           xmpString +
                           "\n<?xpacket end=\"w\"?>";
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(xmpPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Write without BOM to avoid parsing issues
            var encoding = new UTF8Encoding(false); // false = no BOM
            await File.WriteAllTextAsync(xmpPath, xmpString, encoding);
        }

        private bool TryGetProperty(IXmpMeta xmpMeta, string namespaceUri, string propertyName, out string value)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(namespaceUri, propertyName))
                {
                    value = xmpMeta.GetPropertyString(namespaceUri, propertyName);
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch
            {
                // Property exists but couldn't retrieve as string
            }

            value = null;
            return false;
        }

        private bool TryGetPropertyDouble(IXmpMeta xmpMeta, string namespaceUri, string propertyName, out double value)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(namespaceUri, propertyName))
                {
                    value = xmpMeta.GetPropertyDouble(namespaceUri, propertyName);
                    return true;
                }
            }
            catch
            {
                // Property exists but couldn't parse as double
            }

            value = 0;
            return false;
        }

        private bool TryGetPropertyInt(IXmpMeta xmpMeta, string namespaceUri, string propertyName, out int value)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(namespaceUri, propertyName))
                {
                    value = xmpMeta.GetPropertyInteger(namespaceUri, propertyName);
                    return true;
                }
            }
            catch
            {
                // Property exists but couldn't parse as int
            }

            value = 0;
            return false;
        }

        private bool TryGetPropertyBool(IXmpMeta xmpMeta, string namespaceUri, string propertyName, out bool value)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(namespaceUri, propertyName))
                {
                    value = xmpMeta.GetPropertyBoolean(namespaceUri, propertyName);
                    return true;
                }
            }
            catch
            {
                // Property exists but couldn't parse as bool
            }

            value = false;
            return false;
        }

        private string GetPropertySafe(IXmpMeta xmpMeta, string namespaceUri, string propertyName)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(namespaceUri, propertyName))
                {
                    return xmpMeta.GetPropertyString(namespaceUri, propertyName);
                }
            }
            catch
            {
                // Property exists but couldn't retrieve
            }

            return null;
        }

        private List<string> ReadArrayProperty(IXmpMeta xmpMeta, string namespaceUri, string propertyName)
        {
            var result = new List<string>();
            try
            {
                var count = xmpMeta.CountArrayItems(namespaceUri, propertyName);
                for (int i = 1; i <= count; i++) // XMP arrays are 1-indexed
                {
                    var item = xmpMeta.GetArrayItem(namespaceUri, propertyName, i);
                    if (!string.IsNullOrEmpty(item?.Value))
                    {
                        result.Add(item.Value);
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }
            return result;
        }

        private Dictionary<string, object> ReadExtendedData(IXmpMeta xmpMeta)
        {
            var result = new Dictionary<string, object>();
            try
            {
                foreach (var prop in xmpMeta.Properties)
                {
                    if (prop.Path?.Contains($"{QuickCullPrefix}:ExtendedData_") == true)
                    {
                        var key = prop.Path.Substring(prop.Path.LastIndexOf("ExtendedData_") + "ExtendedData_".Length);
                        result[key] = prop.Value;
                    }
                }
            }
            catch
            {
                // Return empty dictionary on error
            }
            return result;
        }

        private int GetRatingFromXmp(IXmpMeta xmpMeta)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(XmpNamespace, "Rating"))
                {
                    return xmpMeta.GetPropertyInteger(XmpNamespace, "Rating");
                }
            }
            catch
            {
                // Property exists but couldn't parse
            }

            return 0;
        }

        private int? GetNullableRating(IXmpMeta xmpMeta)
        {
            try
            {
                if (xmpMeta.DoesPropertyExist(XmpNamespace, "Rating"))
                {
                    var rating = xmpMeta.GetPropertyInteger(XmpNamespace, "Rating");
                    return rating >= 0 && rating <= 5 ? rating : null;
                }
            }
            catch
            {
                // Property exists but couldn't parse
            }

            return null;
        }

        private bool? GetPickStatus(IXmpMeta xmpMeta)
        {
            try
            {
                // Check xmpDM:good property first (Lightroom standard)
                if (TryGetPropertyBool(xmpMeta, XmpDynamicMediaNamespace, "good", out bool pickValue))
                {
                    return pickValue;
                }

                // Fallback: infer from rating
                var rating = GetRatingFromXmp(xmpMeta);
                if (rating > 0)
                    return true;

                return null; // Unpicked
            }
            catch
            {
                return null;
            }
        }

        private bool HasCameraRawSettings(IXmpMeta xmpMeta)
        {
            try
            {
                foreach (var prop in xmpMeta.Properties)
                {
                    if (prop.Path?.StartsWith($"crs:") == true)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
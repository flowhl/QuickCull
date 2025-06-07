using QuickCull.Models;
using QuickCull.Core.Services.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XmpCore;
using XmpCore.Impl;
using XmpCore.Options;

namespace QuickCull.Core.Services.XMP
{
    public class XmpService : IXmpService
    {
        private const string CullingNamespace = "http://yourapp.com/culling/1.0/";
        private const string CullingPrefix = "culling";

        // Fixed: Use Camera Raw Settings namespace instead of Lightroom
        private const string CameraRawNamespace = "http://ns.adobe.com/camera-raw-settings/1.0/";
        private const string CameraRawPrefix = "crs";

        // Additional namespaces that might contain rating info
        private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
        private const string XmpPrefix = "xmp";

        // XMP Dynamic Media namespace for picks/rejects
        private const string XmpDmNamespace = "http://ns.adobe.com/xmp/1.0/DynamicMedia/";
        private const string XmpDmPrefix = "xmpDM";

        public XmpService()
        {
            // Register custom namespace for our analysis data
            XmpMetaFactory.SchemaRegistry.RegisterNamespace(CullingNamespace, CullingPrefix);
        }

        public async Task WriteAnalysisToXmpAsync(string imagePath, AnalysisResult analysisResult)
        {
            string xmpPath = FileServiceProvider.FileService.GetXmpPath(imagePath);

            try
            {
                // Read existing XMP or create new
                var xmp = await ReadOrCreateXmpAsync(xmpPath);

                // Write analysis data to our custom namespace
                xmp.SetProperty(CullingNamespace, "analysisVersion", analysisResult.AnalysisVersion);
                xmp.SetProperty(CullingNamespace, "analysisDate", analysisResult.AnalyzedAt.ToString("O"));
                xmp.SetProperty(CullingNamespace, "modelVersion", analysisResult.ModelVersion);

                // Sharpness data
                xmp.SetPropertyDouble(CullingNamespace, "sharpnessOverall", analysisResult.SharpnessOverall);
                xmp.SetPropertyDouble(CullingNamespace, "sharpnessSubject", analysisResult.SharpnessSubject);

                // Subject detection
                xmp.SetPropertyInteger(CullingNamespace, "subjectCount", analysisResult.SubjectCount);
                xmp.SetPropertyDouble(CullingNamespace, "subjectSharpnessPercentage", analysisResult.SubjectSharpnessPercentage);

                if (analysisResult.SubjectTypes?.Any() == true)
                {
                    var subjectTypesJson = JsonSerializer.Serialize(analysisResult.SubjectTypes);
                    xmp.SetProperty(CullingNamespace, "subjectTypes", subjectTypesJson);
                }

                // Eye detection
                xmp.SetPropertyBoolean(CullingNamespace, "eyesOpen", analysisResult.EyesOpen);
                xmp.SetPropertyDouble(CullingNamespace, "eyeConfidence", analysisResult.EyeConfidence);

                // AI predictions
                xmp.SetPropertyInteger(CullingNamespace, "predictedRating", analysisResult.PredictedRating);
                xmp.SetPropertyDouble(CullingNamespace, "predictionConfidence", analysisResult.PredictionConfidence);

                // Extended data
                if (analysisResult.ExtendedData?.Any() == true)
                {
                    var extendedJson = JsonSerializer.Serialize(analysisResult.ExtendedData);
                    xmp.SetProperty(CullingNamespace, "extendedData", extendedJson);
                }

                // Write XMP to file
                var xmpString = XmpMetaFactory.SerializeToString(xmp, new SerializeOptions
                {
                    UseCompactFormat = false,
                    Indent = "  ",
                    Newline = "\n"
                });

                await File.WriteAllTextAsync(xmpPath, xmpString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to write XMP for {imagePath}", ex);
            }
        }

        public async Task<AnalysisResult> ReadAnalysisFromXmpAsync(string imagePath)
        {
            string xmpPath = FileServiceProvider.FileService.GetXmpPath(imagePath);

            if (!File.Exists(xmpPath))
                return null;

            try
            {
                var xmpString = await File.ReadAllTextAsync(xmpPath);
                var xmp = XmpMetaFactory.ParseFromString(xmpString);

                var result = new AnalysisResult
                {
                    Filename = Path.GetFileName(imagePath)
                };

                // Read analysis metadata
                if (xmp.DoesPropertyExist(CullingNamespace, "analysisVersion"))
                    result.AnalysisVersion = xmp.GetPropertyString(CullingNamespace, "analysisVersion");

                if (xmp.DoesPropertyExist(CullingNamespace, "analysisDate"))
                {
                    var dateString = xmp.GetPropertyString(CullingNamespace, "analysisDate");
                    if (DateTime.TryParse(dateString, out var analysisDate))
                        result.AnalyzedAt = analysisDate;
                }

                if (xmp.DoesPropertyExist(CullingNamespace, "modelVersion"))
                    result.ModelVersion = xmp.GetPropertyString(CullingNamespace, "modelVersion");

                // Read sharpness data
                if (xmp.DoesPropertyExist(CullingNamespace, "sharpnessOverall"))
                    result.SharpnessOverall = xmp.GetPropertyDouble(CullingNamespace, "sharpnessOverall");

                if (xmp.DoesPropertyExist(CullingNamespace, "sharpnessSubject"))
                    result.SharpnessSubject = xmp.GetPropertyDouble(CullingNamespace, "sharpnessSubject");

                // Read subject detection
                if (xmp.DoesPropertyExist(CullingNamespace, "subjectCount"))
                    result.SubjectCount = xmp.GetPropertyInteger(CullingNamespace, "subjectCount");

                if (xmp.DoesPropertyExist(CullingNamespace, "subjectSharpnessPercentage"))
                    result.SubjectSharpnessPercentage = xmp.GetPropertyDouble(CullingNamespace, "subjectSharpnessPercentage");

                if (xmp.DoesPropertyExist(CullingNamespace, "subjectTypes"))
                {
                    var subjectTypesJson = xmp.GetPropertyString(CullingNamespace, "subjectTypes");
                    try
                    {
                        result.SubjectTypes = JsonSerializer.Deserialize<List<string>>(subjectTypesJson) ?? new List<string>();
                    }
                    catch
                    {
                        result.SubjectTypes = new List<string>();
                    }
                }

                // Read eye detection
                if (xmp.DoesPropertyExist(CullingNamespace, "eyesOpen"))
                    result.EyesOpen = xmp.GetPropertyBoolean(CullingNamespace, "eyesOpen");

                if (xmp.DoesPropertyExist(CullingNamespace, "eyeConfidence"))
                    result.EyeConfidence = xmp.GetPropertyDouble(CullingNamespace, "eyeConfidence");

                // Read AI predictions
                if (xmp.DoesPropertyExist(CullingNamespace, "predictedRating"))
                    result.PredictedRating = xmp.GetPropertyInteger(CullingNamespace, "predictedRating");

                if (xmp.DoesPropertyExist(CullingNamespace, "predictionConfidence"))
                    result.PredictionConfidence = xmp.GetPropertyDouble(CullingNamespace, "predictionConfidence");

                // Read extended data
                if (xmp.DoesPropertyExist(CullingNamespace, "extendedData"))
                {
                    var extendedJson = xmp.GetPropertyString(CullingNamespace, "extendedData");
                    try
                    {
                        result.ExtendedData = JsonSerializer.Deserialize<Dictionary<string, object>>(extendedJson) ?? new Dictionary<string, object>();
                    }
                    catch
                    {
                        result.ExtendedData = new Dictionary<string, object>();
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read XMP for {imagePath}", ex);
            }
        }

        public async Task<XmpMetadata> ReadAllXmpDataAsync(string imagePath)
        {
            string xmpPath = FileServiceProvider.FileService.GetXmpPath(imagePath);

            if (!File.Exists(xmpPath))
                return new XmpMetadata();

            try
            {
                var xmpString = await File.ReadAllTextAsync(xmpPath);
                var xmp = XmpMetaFactory.ParseFromString(xmpString);

                var metadata = new XmpMetadata
                {
                    LastModified = File.GetLastWriteTime(xmpPath),
                    AnalysisData = await ReadAnalysisFromXmpAsync(imagePath)
                };

                // Read Adobe Camera Raw/Lightroom data
                // Note: In your XMP file, there's no explicit rating, but there are other properties

                // Try to read rating from XMP basic namespace first
                if (xmp.DoesPropertyExist(XmpNamespace, "Rating"))
                {
                    metadata.LightroomRating = xmp.GetPropertyInteger(XmpNamespace, "Rating");
                }

                // Read pick/reject status from XMP Dynamic Media namespace
                if (xmp.DoesPropertyExist(XmpDmNamespace, "good"))
                {
                    var goodValue = xmp.GetPropertyString(XmpDmNamespace, "good");
                    if (bool.TryParse(goodValue, out bool isGood))
                    {
                        metadata.LightroomPick = isGood;
                    }
                }
                // If xmpDm:good doesn't exist, LightroomPick remains null (default/unpicked state)

                // Check for Camera Raw specific properties that might indicate editing
                // Your file shows extensive Camera Raw adjustments but no explicit rating
                bool hasAdjustments = false;

                // Check for common adjustment properties
                var adjustmentProperties = new[]
                {
                    "Exposure2012", "Contrast2012", "Highlights2012", "Shadows2012",
                    "Whites2012", "Blacks2012", "Vibrance", "Saturation"
                };

                foreach (var prop in adjustmentProperties)
                {
                    if (xmp.DoesPropertyExist(CameraRawNamespace, prop))
                    {
                        var value = xmp.GetPropertyString(CameraRawNamespace, prop);
                        if (!string.IsNullOrEmpty(value) && value != "0")
                        {
                            hasAdjustments = true;
                            break;
                        }
                    }
                }

                // Store Camera Raw settings that might be useful
                if (xmp.DoesPropertyExist(CameraRawNamespace, "HasSettings"))
                {
                    metadata.HasCameraRawAdjustments = xmp.GetPropertyBoolean(CameraRawNamespace, "HasSettings");
                }

                if (xmp.DoesPropertyExist(CameraRawNamespace, "AlreadyApplied"))
                {
                    metadata.AdjustmentsApplied = xmp.GetPropertyBoolean(CameraRawNamespace, "AlreadyApplied");
                }

                // Read other potentially useful metadata
                if (xmp.DoesPropertyExist(CameraRawNamespace, "CameraProfile"))
                {
                    metadata.CameraProfile = xmp.GetPropertyString(CameraRawNamespace, "CameraProfile");
                }

                return metadata;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read XMP metadata for {imagePath}", ex);
            }
        }

        public bool XmpFileExists(string imagePath)
        {
            string xmpPath = FileServiceProvider.FileService.GetXmpPath(imagePath);
            return File.Exists(xmpPath);
        }

        public async Task<DateTime?> GetXmpModifiedDateAsync(string imagePath)
        {
            string xmpPath = FileServiceProvider.FileService.GetXmpPath(imagePath);

            if (!File.Exists(xmpPath))
                return null;

            return await Task.FromResult(File.GetLastWriteTime(xmpPath));
        }

        private async Task<IXmpMeta> ReadOrCreateXmpAsync(string xmpPath)
        {
            if (File.Exists(xmpPath))
            {
                var xmpString = await File.ReadAllTextAsync(xmpPath);
                return XmpMetaFactory.ParseFromString(xmpString);
            }
            else
            {
                return XmpMetaFactory.Create();
            }
        }
    }
}
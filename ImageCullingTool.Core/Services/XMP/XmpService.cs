using ImageCullingTool.Models;
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

namespace ImageCullingTool.Services.XMP
{
    public class XmpService : IXmpService
    {
        private const string CullingNamespace = "http://yourapp.com/culling/1.0/";
        private const string CullingPrefix = "culling";
        private const string LightroomNamespace = "http://ns.adobe.com/lightroom/1.0/";
        private const string LightroomPrefix = "lr";

        public XmpService()
        {
            // Register custom namespace for our analysis data
            XmpMetaFactory.SchemaRegistry.RegisterNamespace(CullingNamespace, CullingPrefix);
        }

        public async Task WriteAnalysisToXmpAsync(string imagePath, AnalysisResult analysisResult)
        {
            var xmpPath = imagePath + ".xmp";

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
            var xmpPath = imagePath + ".xmp";

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
            var xmpPath = imagePath + ".xmp";

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

                // Read Lightroom data (read-only for our app)
                if (xmp.DoesPropertyExist(LightroomNamespace, "rating"))
                    metadata.LightroomRating = xmp.GetPropertyInteger(LightroomNamespace, "rating");

                if (xmp.DoesPropertyExist(LightroomNamespace, "pick"))
                    metadata.LightroomPick = xmp.GetPropertyBoolean(LightroomNamespace, "pick");

                if (xmp.DoesPropertyExist(LightroomNamespace, "label"))
                    metadata.LightroomLabel = xmp.GetPropertyString(LightroomNamespace, "label");

                return metadata;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read XMP metadata for {imagePath}", ex);
            }
        }

        public bool XmpFileExists(string imagePath)
        {
            return File.Exists(imagePath + ".xmp");
        }

        public async Task<DateTime?> GetXmpModifiedDateAsync(string imagePath)
        {
            var xmpPath = imagePath + ".xmp";
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
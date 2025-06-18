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
using QuickCull.Core.Extensions;

namespace QuickCull.Core.Services.XMP
{
    public class XmpService : IXmpService
    {
        private const string CullingNamespace = "http://yourapp.com/culling/1.0/";
        private const string CullingPrefix = "culling";

        // Standard Adobe namespaces - these should be pre-registered but let's be explicit
        private const string CameraRawNamespace = "http://ns.adobe.com/camera-raw-settings/1.0/";
        private const string CameraRawPrefix = "crs";
        private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
        private const string XmpPrefix = "xmp";
        private const string XmpDmNamespace = "http://ns.adobe.com/xmp/1.0/DynamicMedia/";
        private const string XmpDmPrefix = "xmpDM";

        public XmpService()
    {
        // Only register our custom namespace - Adobe namespaces should already be registered
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

                //Group-ID
                xmp.SetPropertyInteger(CullingNamespace, "groupID", analysisResult.GroupID);

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

        /// <summary>
        /// Write pick/reject status to XMP in Lightroom-compatible format
        /// </summary>
        public async Task WritePickStatusToXmpAsync(string imagePath, bool? pickStatus)
        {
            string xmpPath = FileServiceProvider.FileService.GetXmpPath(imagePath);

            try
            {
                // Read existing XMP or create new
                var xmp = await ReadOrCreateXmpAsync(xmpPath);

                // Write pick status using multiple Lightroom-compatible properties
                if (pickStatus.HasValue)
                {
                    // Primary pick flag - used by Lightroom and other Adobe products
                    xmp.SetPropertyBoolean(XmpDmNamespace, "good", pickStatus.Value);

                    // Alternative format some tools use
                    if (pickStatus.Value)
                    {
                        xmp.SetPropertyInteger(XmpNamespace, "Rating", 1); // Some tools use rating for pick
                        xmp.SetProperty(XmpNamespace, "Label", "Select"); // Alternative label approach
                    }
                    else
                    {
                        xmp.SetPropertyInteger(XmpNamespace, "Rating", -1); // Negative rating for reject
                        xmp.SetProperty(XmpNamespace, "Label", "Reject");
                    }

                    // Adobe Camera Raw specific pick flag
                    xmp.SetPropertyBoolean(CameraRawNamespace, "Selected", pickStatus.Value);
                }
                else
                {
                    // Clear all pick/reject indicators
                    if (xmp.DoesPropertyExist(XmpDmNamespace, "good"))
                        xmp.DeleteProperty(XmpDmNamespace, "good");

                    if (xmp.DoesPropertyExist(XmpNamespace, "Rating"))
                        xmp.DeleteProperty(XmpNamespace, "Rating");

                    if (xmp.DoesPropertyExist(XmpNamespace, "Label"))
                        xmp.DeleteProperty(XmpNamespace, "Label");

                    if (xmp.DoesPropertyExist(CameraRawNamespace, "Selected"))
                        xmp.DeleteProperty(CameraRawNamespace, "Selected");
                }

                // Add timestamp for when the pick status was set
                xmp.SetProperty(XmpNamespace, "ModifyDate", DateTime.Now.ToString("O"));

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
                string msg = ex.GetFullDetails();
                throw new InvalidOperationException($"Failed to write pick status to XMP for {imagePath}", ex);
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
                    Filename = Path.GetFileName(imagePath),
                    FilePath = imagePath
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

                // Read Group ID
                if (xmp.DoesPropertyExist(CullingNamespace, "groupID"))
                    result.GroupID = xmp.GetPropertyInteger(CullingNamespace, "groupID");

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
                // Try to read rating from XMP basic namespace first
                if (xmp.DoesPropertyExist(XmpNamespace, "Rating"))
                {
                    var rating = xmp.GetPropertyInteger(XmpNamespace, "Rating");
                    // Only use positive ratings as actual star ratings, ignore pick/reject ratings
                    if (rating > 0 && rating <= 5)
                    {
                        metadata.LightroomRating = rating;
                    }
                }

                // Read pick/reject status from multiple possible locations
                bool? pickStatus = null;

                // Primary: XMP Dynamic Media namespace
                if (xmp.DoesPropertyExist(XmpDmNamespace, "good"))
                {
                    pickStatus = xmp.GetPropertyBoolean(XmpDmNamespace, "good");
                }
                // Alternative: Camera Raw Selected property
                else if (xmp.DoesPropertyExist(CameraRawNamespace, "Selected"))
                {
                    pickStatus = xmp.GetPropertyBoolean(CameraRawNamespace, "Selected");
                }
                // Alternative: Check for pick/reject labels
                else if (xmp.DoesPropertyExist(XmpNamespace, "Label"))
                {
                    var label = xmp.GetPropertyString(XmpNamespace, "Label");
                    if (label.Equals("Select", StringComparison.OrdinalIgnoreCase))
                        pickStatus = true;
                    else if (label.Equals("Reject", StringComparison.OrdinalIgnoreCase))
                        pickStatus = false;
                }

                metadata.LightroomPick = pickStatus;

                // Check for Camera Raw specific properties that might indicate editing
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
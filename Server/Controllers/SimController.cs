using AdminDashTemplate.Server.Data;
using Assimp;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace AdminDashTemplate.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class SimController : ControllerBase
    {
        private readonly AVRContext _context;
        private readonly ILogger<SimController> _logger;
        public SimController(AVRContext context, ILogger<SimController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("test")]
        public IActionResult test()
        {
            return Content("Test successful");
        }

        [HttpGet("TotalReputation")] // Matches the endpoint used in Analytics.razor
        public async Task<ActionResult<int>> GetTotalReputation()
        {
            try
            {
                // Access the database and calculate the sum
                // The SumAsync is the most efficient way to do this in EF Core
                var totalReputation = await _context.Users.SumAsync(u => u.Reputation);
                return totalReputation;
            }
            catch (Exception ex)
            {
                // Log and return a server error
                Console.WriteLine($"Database Error: {ex.Message}");
                return StatusCode(500, "Error calculating total reputation.");
            }
        }

        // --- NEW ENDPOINT FOR CONTINUING EDUCATION HOURS ---
        [HttpGet("TotalContinuingEducation")]
        public async Task<ActionResult<int>> GetTotalContinuingEducation()
        {
            try
            {
                // Access the UserProfiles table and sum the ContinuingEducationHours column
                var totalHours = await _context.UserProfiles.SumAsync(p => p.ContinuingEducationHours);
                // Note: We return the total hours directly as the integer sum
                return totalHours;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database Error: {ex.Message}");
                return StatusCode(500, "Error calculating total continuing education hours.");
            }
        }

        [HttpPost("ConvertModel")]
        public async Task<IActionResult> ConvertModel()
        {
            // Use the inherited 'Request' property from ControllerBase
            if (!Request.HasFormContentType || !Request.Form.Files.Any())
            {
                return BadRequest("No file found in the request.");
            }

            // Get the uploaded file (your logic already used the first file)
            var file = Request.Form.Files[0];
            _logger.LogWarning($"Processing request for file: {file.FileName}");

            // NOTE: Connection string should be retrieved from configuration!
            string connectionString = "DefaultEndpointsProtocol=https;AccountName=recitestorage;AccountKey=lgaAm9qT8uIrBZCx49dnvhW6rTqL5PGkX0Q1C6MPm88MIW19WNdoLKda4ziYhfu9tWxy/u6kdfGr+AStIrQFxw==;EndpointSuffix=core.windows.net";
            string containerName = "models";
            string convertedGlbUrl = string.Empty;
            string originalZipUrl = string.Empty;

            // Use a secure temporary directory path
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string modelFilePath = null;
            string finalGlbFilePath = null;
            bool foundExistingGlb = false;

            try
            {
                // 1. Setup Temporary Directory
                Directory.CreateDirectory(tempDir);

                // 2. Create a memory stream and copy the uploaded ZIP file to it
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // 3. Initialize Blob Storage & Upload Original ZIP
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                // It's generally better to create the container outside the hot path
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

                var zipFileName = $"raw/{Guid.NewGuid()}.zip";
                var zipBlobClient = containerClient.GetBlobClient(zipFileName);
                await zipBlobClient.UploadAsync(memoryStream, true);
                _logger.LogWarning($"Original zip uploaded to blob: {zipBlobClient.Uri.AbsoluteUri}");
                originalZipUrl = zipBlobClient.Uri.AbsoluteUri;
                memoryStream.Position = 0;

                // 4. Unzip and Extract Model File
                using var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Read);
                foreach (var entry in zipArchive.Entries)
                {
                    string fileExtension = Path.GetExtension(entry.FullName).ToLowerInvariant();
                    // ... (Your existing logic for extracting files and finding modelFilePath remains correct)
                    if (fileExtension == ".fbx" || fileExtension == ".obj" || fileExtension == ".glb" || fileExtension == ".mtl" ||
                        fileExtension == ".jpg" || fileExtension == ".png" || fileExtension == ".jpeg")
                    {
                        // Create subdirectory structure to avoid path conflicts in tempDir
                        // This uses entry.FullName to maintain path structure if files are nested.
                        string destinationPath = Path.Combine(tempDir, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                        // Only extract if it's a file, not a directory entry
                        if (!string.IsNullOrEmpty(entry.Name))
                        {
                            entry.ExtractToFile(destinationPath, true);
                        }

                        if (fileExtension == ".fbx" || fileExtension == ".obj" || fileExtension == ".glb")
                        {
                            // Set the primary model file path
                            modelFilePath = destinationPath;
                        }
                    }
                }

                if (string.IsNullOrEmpty(modelFilePath))
                {
                    return NotFound("No supported 3D model file (.glb, .fbx, or .obj) found in the zip archive.");
                }

                string modelFileExtension = Path.GetExtension(modelFilePath).ToLowerInvariant();
                finalGlbFilePath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(modelFilePath) + ".glb");

                // 5. Model Conversion Logic
                if (modelFileExtension == ".glb")
                {
                    // Already GLB
                    foundExistingGlb = true;
                    System.IO.File.Copy(modelFilePath, finalGlbFilePath, true);
                    _logger.LogWarning("Model is already a GLB, skipping conversion.");
                }
                else if (modelFileExtension == ".fbx")
                {
                    // FBX Conversion via Native Executable (FBX2glTF)

                    // *** CRITICAL CHANGE: Use AppContext.BaseDirectory or IWebHostEnvironment ***
                    // to correctly locate deployed files in the App Service wwwroot.
                    string fbx2gltfPath = Path.Combine(AppContext.BaseDirectory, "NativeTools", "FBX2glTF.exe");

                    if (!System.IO.File.Exists(fbx2gltfPath))
                    {
                        _logger.LogError($"FBX2glTF executable not found at: {fbx2gltfPath}");
                        throw new FileNotFoundException($"FBX2glTF executable not found. Ensure it is deployed to the NativeTools folder.");
                    }

                    _logger.LogWarning($"Starting FBX conversion using {fbx2gltfPath}...");
                    string arguments = $"--binary --input \"{modelFilePath}\" --output \"{finalGlbFilePath}\"";

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = fbx2gltfPath,
                        Arguments = arguments,
                        WorkingDirectory = tempDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    await process.WaitForExitAsync();

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"FBX2glTF Failed. Exit Code: {process.ExitCode}, Error: {error}");
                        throw new Exception($"FBX conversion failed. Error: {error}. Output: {output}");
                    }
                    _logger.LogWarning("Conversion to .glb complete via FBX2glTF.");
                }
                else if (modelFileExtension == ".obj")
                {
                    // Fallback to Assimp for OBJ (if you wish to keep it, otherwise use SharpGLTF)
                    _logger.LogWarning("Converting OBJ to GLB using Assimp...");

                    using (var importer = new AssimpContext())
                    {
                        // Ensure the full path to the directory is set for texture search
                        importer.SetConfig(new Assimp.Configs.StringPropertyConfig("PP_PTV_ROOT", tempDir));

                        var scene = importer.ImportFile(modelFilePath, PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals);
                        if (scene == null) throw new Exception("Assimp failed to import OBJ file.");

                        importer.ExportFile(scene, finalGlbFilePath, "glb");
                        _logger.LogWarning("OBJ conversion to .glb complete via Assimp.");
                    }
                }
                else
                {
                    return new BadRequestObjectResult($"Unsupported model format: {modelFileExtension}");
                }

                // 6. Upload the converted .glb file to Blob Storage
                string glbBlobFileName = $"converted/{Guid.NewGuid()}.glb";
                var glbBlobClient = containerClient.GetBlobClient(glbBlobFileName);

                using var glbStream = System.IO.File.OpenRead(finalGlbFilePath);
                await glbBlobClient.UploadAsync(glbStream, true);
                convertedGlbUrl = glbBlobClient.Uri.AbsoluteUri;
                _logger.LogWarning($"Converted GLB uploaded to blob: {convertedGlbUrl}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during file processing.");
                return new ObjectResult("An error occurred: " + ex.Message)
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
            finally
            {
                // Clean up the temporary directory and its contents
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    _logger.LogWarning($"Cleaned up temp directory: {tempDir}");
                }
            }

            // 7. Return the link to the uploaded .glb file
            return new OkObjectResult(new ConvertedModelResponse
            {
                OriginalZipUrl = originalZipUrl,
                ConvertedGlbUrl = convertedGlbUrl,
                WasConverted = !foundExistingGlb
            });
        }
    }

    public class ConvertedModelResponse
    {
        public string OriginalZipUrl { get; set; }
        public string ConvertedGlbUrl { get; set; }
        public bool WasConverted { get; set; }  // New property
    }
}

using QuickCull.Core.Services.Logging;
using QuickCull.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace QuickCull.Core.Services.Cache
{
    public class CullingDbContext : DbContext
    {
        private readonly string _dbPath;
        private readonly ILoggingService _loggingService;

        public CullingDbContext(string folderPath, ILoggingService loggingService = null)
        {
            _dbPath = Path.Combine(folderPath, ".culling_cache.db");
            _loggingService = loggingService;

            // Ensure database directory exists
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public DbSet<ImageAnalysis> Images { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath};Pooling=False");

            // Optional: Enable detailed logging in debug mode
#if DEBUG
            if (_loggingService != null)
            {
                optionsBuilder.EnableSensitiveDataLogging();
                optionsBuilder.LogTo(x => _loggingService.LogInfoAsync(x), Microsoft.Extensions.Logging.LogLevel.Information);
            }
#endif
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ImageAnalysis>(entity =>
            {
                // Primary key
                entity.HasKey(e => e.Filename);
                entity.Property(e => e.Filename)
                    .HasMaxLength(260) // Windows path limit
                    .IsRequired();

                // String properties with reasonable limits (nullable)
                entity.Property(e => e.ImageFormat).HasMaxLength(10).IsRequired(false);
                entity.Property(e => e.FileHash).HasMaxLength(32).IsRequired(false);
                entity.Property(e => e.AnalysisVersion).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ModelVersion).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.SubjectTypes).HasMaxLength(500).IsRequired(false);
                entity.Property(e => e.LightroomLabel).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ColorAnalysis).HasMaxLength(1000).IsRequired(false);
                entity.Property(e => e.ExtendedAnalysisJson).HasColumnType("TEXT").IsRequired(false);

                // Indexes for common query patterns
                entity.HasIndex(e => e.AnalysisDate)
                    .HasDatabaseName("IX_Images_AnalysisDate");

                entity.HasIndex(e => e.PredictedRating)
                    .HasDatabaseName("IX_Images_PredictedRating");

                entity.HasIndex(e => e.SharpnessOverall)
                    .HasDatabaseName("IX_Images_SharpnessOverall");

                entity.HasIndex(e => e.LightroomRating)
                    .HasDatabaseName("IX_Images_LightroomRating");

                entity.HasIndex(e => e.IsRaw)
                    .HasDatabaseName("IX_Images_IsRaw");

                entity.HasIndex(e => e.HasXmp)
                    .HasDatabaseName("IX_Images_HasXmp");

                // Composite index for common filter combinations
                entity.HasIndex(e => new { e.PredictedRating, e.SharpnessOverall })
                    .HasDatabaseName("IX_Images_Rating_Sharpness");
            });
        }

        /// <summary>
        /// Ensures the database exists and applies any pending migrations.
        /// If the database schema is incompatible with the current model, deletes and recreates it.
        /// </summary>
        public async Task EnsureDatabaseCreatedAsync()
        {
            try
            {
                // First try to ensure the database is created
                var created = await Database.EnsureCreatedAsync();

                if (!created)
                {
                    // Database already exists, test if it's compatible with current model
                    await TestDatabaseCompatibilityAsync();
                }
            }
            catch (Exception ex)
            {
                // If any error occurs (schema mismatch, corruption, etc.), signal that recreation is needed
                if (_loggingService != null)
                {
                    await _loggingService.LogWarningAsync($"Database compatibility issue detected: {ex.Message}");
                }

                // Throw a specific exception to signal that recreation is needed
                throw new DatabaseRecreationRequiredException($"Database recreation required: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Tests if the existing database is compatible with the current model
        /// </summary>
        private async Task TestDatabaseCompatibilityAsync()
        {
            try
            {
                // Use reflection to get all properties of ImageAnalysis
                var imageAnalysisType = typeof(ImageAnalysis);
                var properties = imageAnalysisType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(p => p.CanRead && p.CanWrite) // Only include readable/writable properties
                    .ToList();

                if (_loggingService != null)
                {
                    await _loggingService.LogInfoAsync($"Testing database compatibility for {properties.Count} properties...");
                }

                // Try to query all properties using reflection
                // This will fail if any column is missing or has an incompatible type
                var query = Images.AsNoTracking().Take(1);

                // Execute the query and try to access all properties
                var testItems = await query.ToListAsync();

                if (testItems.Any())
                {
                    var testItem = testItems.First();

                    // Try to access each property - this will throw if column doesn't exist or types don't match
                    foreach (var property in properties)
                    {
                        try
                        {
                            var value = property.GetValue(testItem);
                            // Successfully accessed the property
                        }
                        catch (Exception propEx)
                        {
                            throw new InvalidOperationException($"Property '{property.Name}' is incompatible: {propEx.Message}", propEx);
                        }
                    }
                }

                if (_loggingService != null)
                {
                    await _loggingService.LogInfoAsync("Database schema compatibility test passed");
                }
            }
            catch (Exception ex)
            {
                // Schema is incompatible, throw to trigger recreation
                throw new InvalidOperationException($"Database schema is incompatible with current model: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Static method to recreate the database file completely
        /// This is called from outside the context to avoid file handle conflicts
        /// </summary>
        public static async Task RecreateDatabase(string folderPath, ILoggingService loggingService = null)
        {
            var dbPath = Path.Combine(folderPath, ".culling_cache.db");

            try
            {
                if (loggingService != null)
                {
                    await loggingService.LogInfoAsync($"Recreating database at {dbPath}");
                }

                // Step 1: Try multiple times to delete the file with retries
                await DeleteDatabaseFileWithRetry(dbPath, maxRetries: 15, delayMs: 100);

                // Step 2: Create a new context to create the database
                using (var newContext = new CullingDbContext(folderPath, loggingService))
                {
                    var created = await newContext.Database.EnsureCreatedAsync();
                    if (created)
                    {
                        if (loggingService != null)
                        {
                            await loggingService.LogInfoAsync("New database created successfully");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Failed to create new database");
                    }
                }
            }
            catch (Exception ex)
            {
                if (loggingService != null)
                {
                    await loggingService.LogErrorAsync($"Failed to recreate database at {dbPath}", ex);
                }
                throw new InvalidOperationException($"Failed to recreate database at {dbPath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Attempts to delete the database file with retries to handle file locking issues
        /// </summary>
        private static async Task DeleteDatabaseFileWithRetry(string dbPath, int maxRetries = 15, int delayMs = 100)
        {
            var attempt = 0;
            var lastException = (Exception)null;

            while (attempt < maxRetries)
            {
                try
                {
                    // Force garbage collection before each attempt
                    if (attempt > 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }

                    if (File.Exists(dbPath))
                    {
                        // Try to delete related SQLite files as well
                        var relatedFiles = new[]
                        {
                            dbPath,                    // Main database file
                            dbPath + "-wal",           // Write-Ahead Log
                            dbPath + "-shm",           // Shared Memory
                            dbPath + "-journal"        // Journal file
                        };

                        foreach (var file in relatedFiles)
                        {
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                            }
                        }

                        Console.WriteLine("Database files deleted successfully");
                        return; // Success
                    }
                    else
                    {
                        Console.WriteLine("Database file does not exist, skipping deletion");
                        return; // File doesn't exist, consider it success
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    if (attempt < maxRetries)
                    {
                        Console.WriteLine($"Attempt {attempt} to delete database failed: {ex.Message}. Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);

                        // Increase delay for next attempt (but not too much for faster retry)
                        delayMs = Math.Min(delayMs + 50, 1000); // Cap at 1 second
                    }
                }
            }

            // If we get here, all retries failed
            throw new InvalidOperationException($"Failed to delete database file after {maxRetries} attempts. Last error: {lastException?.Message}", lastException);
        }

        /// <summary>
        /// Gets the database file path
        /// </summary>
        public string DatabasePath => _dbPath;
    }

    /// <summary>
    /// Exception thrown when database recreation is required
    /// </summary>
    public class DatabaseRecreationRequiredException : Exception
    {
        public DatabaseRecreationRequiredException(string message) : base(message) { }
        public DatabaseRecreationRequiredException(string message, Exception innerException) : base(message, innerException) { }
    }
}
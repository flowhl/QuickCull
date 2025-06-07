using ImageCullingTool.Core.Services.Logging;
using ImageCullingTool.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Core.Services.Cache
{
    public class CullingDbContext : DbContext
    {
        private readonly string _dbPath;
        private readonly ILoggingService _loggingService;

        public CullingDbContext(string folderPath, ILoggingService loggingService)
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
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");

            // Optional: Enable detailed logging in debug mode
#if DEBUG
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.LogTo(x => _loggingService.LogInfoAsync(x), Microsoft.Extensions.Logging.LogLevel.Information);
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
                // If any error occurs (schema mismatch, corruption, etc.), recreate the database
                await _loggingService.LogWarningAsync($"Database compatibility issue detected: {ex.Message}");
                await RecreateDatabase();
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

                await _loggingService.LogInfoAsync($"Testing database compatibility for {properties.Count} properties...");

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

                await _loggingService.LogInfoAsync("Database schema compatibility test passed");
            }
            catch (Exception ex)
            {
                // Schema is incompatible, throw to trigger recreation
                throw new InvalidOperationException($"Database schema is incompatible with current model: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes and recreates the database
        /// </summary>
        private async Task RecreateDatabase()
        {
            try
            {
                await _loggingService.LogInfoAsync($"Recreating database at {_dbPath}");

                // Delete the existing database file
                if (File.Exists(_dbPath))
                {
                    // Close any existing connections first
                    await Database.CloseConnectionAsync();

                    // Small delay to ensure file handle is released
                    await Task.Delay(100);

                    File.Delete(_dbPath);
                    await _loggingService.LogInfoAsync("Old database file deleted");
                }

                // Create new database
                var created = await Database.EnsureCreatedAsync();
                if (created)
                {
                    await _loggingService.LogInfoAsync("New database created successfully");
                }
                else
                {
                    throw new InvalidOperationException("Failed to create new database");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to recreate database at {_dbPath}", ex);
            }
        }

        /// <summary>
        /// Gets the database file path
        /// </summary>
        public string DatabasePath => _dbPath;
    }
}
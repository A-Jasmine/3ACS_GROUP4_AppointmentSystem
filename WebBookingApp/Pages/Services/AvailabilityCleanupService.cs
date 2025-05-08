using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace WebBookingApp.Services
{
    public class AvailabilityCleanupService : BackgroundService
    {
        private readonly ILogger<AvailabilityCleanupService> _logger;
        private readonly string _connectionString;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(24); // Run daily

        public AvailabilityCleanupService(ILogger<AvailabilityCleanupService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Availability Cleanup Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Running availability cleanup...");
                    await CleanUpPastAvailabilities();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during availability cleanup");
                }

                await Task.Delay(_cleanupInterval, stoppingToken);
            }

            _logger.LogInformation("Availability Cleanup Service is stopping.");
        }

        private async Task CleanUpPastAvailabilities()
        {
            using SqlConnection conn = new(_connectionString);
            await conn.OpenAsync();

            // Get today's date (without time component)
            var today = DateTime.Today;

            // Delete from professor_availability
            string deleteProfessorQuery = @"
                DELETE FROM dbo.professor_availability
                WHERE 
                    (available_date IS NOT NULL AND available_date < @today) OR
                    (unavailable_date IS NOT NULL AND unavailable_date < @today)";

            // Delete from AlumniAvailability (updated to match your schema)
            string deleteAlumniQuery = @"
                DELETE FROM dbo.AlumniAvailability
                WHERE available_date < @today";

            // Delete from OrganizationAvailability
            string deleteStudentOrgQuery = @"
                DELETE FROM dbo.OrganizationAvailability
                WHERE 
                    (AvailableDate IS NOT NULL AND AvailableDate < @today) OR
                    (UnavailableDate IS NOT NULL AND UnavailableDate < @today)";

            // Execute all cleanup queries in a transaction
            using (var transaction = conn.BeginTransaction())
            {
                try
                {
                    int totalRowsAffected = 0;

                    // Clean professor availabilities
                    using (SqlCommand cmd = new(deleteProfessorQuery, conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("@today", today);
                        totalRowsAffected += await cmd.ExecuteNonQueryAsync();
                    }

                    // Clean alumni availabilities
                    using (SqlCommand cmd = new(deleteAlumniQuery, conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("@today", today);
                        totalRowsAffected += await cmd.ExecuteNonQueryAsync();
                    }

                    // Clean student org availabilities
                    using (SqlCommand cmd = new(deleteStudentOrgQuery, conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("@today", today);
                        totalRowsAffected += await cmd.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                    _logger.LogInformation($"Cleaned up {totalRowsAffected} past availability records across all user types");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "Error occurred during availability cleanup transaction");
                    throw;
                }
            }
        }
    }
}
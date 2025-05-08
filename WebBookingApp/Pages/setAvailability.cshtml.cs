using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using static WebBookingApp.Pages.makeAppointmentModel;

namespace WebBookingApp.Pages
{
    public class setAvailabilitymodel : PageModel
    {
        private readonly ILogger<setAvailabilitymodel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connString;


        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public byte[]? ProfilePicture { get; set; }
        public string Role { get; set; } = "";

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; set; } = string.Empty;

        public setAvailabilitymodel(ILogger<setAvailabilitymodel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connString = _configuration.GetConnectionString("DefaultConnection");
        }

        private async Task CleanUpPastAvailabilities(int professorId)
        {
            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                string deleteQuery = @"
            DELETE FROM dbo.professor_availability
            WHERE professor_id = @ProfessorId
            AND (
                (available_date IS NOT NULL AND available_date < CAST(GETDATE() AS DATE)) OR
                (unavailable_date IS NOT NULL AND unavailable_date < CAST(GETDATE() AS DATE))
            )";

                using (SqlCommand cmd = new(deleteQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@ProfessorId", professorId);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Removed {rowsAffected} past availabilities");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cleaning up past availabilities: {ex.Message}");
            }
        }
        public async Task<IActionResult> OnPostAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return RedirectToPage("/Login");
            }

            string status = Request.Form["status"];
            string startTime = Request.Form["start_time"];
            string endTime = Request.Form["end_time"];
            string unavailableDate = Request.Form["unavailable_date"];
            string availableDate = Request.Form["available_date"];
            string capacityStr = Request.Form["capacity"];

            // Parse capacity - default to 1 if not provided or invalid
            int capacity = 1;
            if (!string.IsNullOrEmpty(capacityStr))
            {
                int.TryParse(capacityStr, out capacity);
            }


            if (string.IsNullOrEmpty(status))
            {
                _logger.LogWarning("Status not selected");
                return RedirectToPage("/setAvailability");
            }

            if (string.IsNullOrEmpty(unavailableDate) && string.IsNullOrEmpty(availableDate))
            {
                _logger.LogWarning("Both Available and Unavailable dates are empty");
                return RedirectToPage("/setAvailability");
            }

            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                // Get the professor's ID using the email.
                string getProfIdQuery = "SELECT professor_id FROM dbo.userCICT WHERE email = @Email";
                int professorId = 0;
                using (SqlCommand cmd = new(getProfIdQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null) professorId = (int)result;
                    else
                    {
                        _logger.LogWarning("Professor not found");
                        return RedirectToPage("/dashboardProfessor");
                    }
                }

                // Decide on the target date (either available or unavailable date).
                string targetDate = !string.IsNullOrEmpty(availableDate) ? availableDate : unavailableDate;

                // Check if this availability setting already exists for the professor and target date.
                string checkExistingQuery = @"
            SELECT availability_id FROM dbo.professor_availability
            WHERE professor_id = @ProfessorId
            AND (available_date = @TargetDate OR unavailable_date = @TargetDate)";

                int? existingAvailabilityId = null;
                using (SqlCommand checkCmd = new(checkExistingQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@ProfessorId", professorId);
                    checkCmd.Parameters.AddWithValue("@TargetDate", targetDate);

                    var existingId = await checkCmd.ExecuteScalarAsync();
                    if (existingId != null && existingId != DBNull.Value)
                    {
                        existingAvailabilityId = Convert.ToInt32(existingId);
                    }
                }

                if (existingAvailabilityId.HasValue)
                {
                    // Prevent duplicate entry if not explicitly updating an existing one
                    if (status.ToLower() == "available" || status.ToLower() == "unavailable")
                    {
                        _logger.LogWarning("Availability date already exists. Redirecting to error page.");
                        return RedirectToPage("/");  // Possibly show an error message here.
                    }

                    // Update the existing availability entry
                    string updateQuery = @"
                UPDATE dbo.professor_availability
                SET status = @Status, 
                    start_time = @StartTime, 
                    end_time = @EndTime,
                    unavailable_date = @UnavailableDate, 
                    available_date = @AvailableDate,
                    capacity = @Capacity
                WHERE availability_id = @AvailabilityId
                AND professor_id = @ProfessorId";


                    using (SqlCommand updateCmd = new(updateQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@Status", status);
                        updateCmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTime) ? (object)DBNull.Value : startTime);
                        updateCmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTime) ? (object)DBNull.Value : endTime);
                        updateCmd.Parameters.AddWithValue("@UnavailableDate", string.IsNullOrEmpty(unavailableDate) ? (object)DBNull.Value : unavailableDate);
                        updateCmd.Parameters.AddWithValue("@AvailableDate", string.IsNullOrEmpty(availableDate) ? (object)DBNull.Value : availableDate);
                        updateCmd.Parameters.AddWithValue("@Capacity", capacity);
                        updateCmd.Parameters.AddWithValue("@AvailabilityId", existingAvailabilityId.Value);
                        updateCmd.Parameters.AddWithValue("@ProfessorId", professorId);

                        await updateCmd.ExecuteNonQueryAsync();
                        _logger.LogInformation("Availability updated successfully.");
                    }
                }
                else
                {
                    // Insert a new availability setting for the professor
                    string insertQuery = @"
                INSERT INTO dbo.professor_availability 
                (professor_id, status, start_time, end_time, unavailable_date, available_date, capacity)
                VALUES (@ProfessorId, @Status, @StartTime, @EndTime, @UnavailableDate, @AvailableDate, @Capacity)";

                    using (SqlCommand insertCmd = new(insertQuery, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@ProfessorId", professorId);
                        insertCmd.Parameters.AddWithValue("@Status", status);
                        insertCmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTime) ? (object)DBNull.Value : startTime);
                        insertCmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTime) ? (object)DBNull.Value : endTime);
                        insertCmd.Parameters.AddWithValue("@Capacity", capacity);
                        insertCmd.Parameters.AddWithValue("@UnavailableDate", string.IsNullOrEmpty(unavailableDate) ? (object)DBNull.Value : unavailableDate);
                        insertCmd.Parameters.AddWithValue("@AvailableDate", string.IsNullOrEmpty(availableDate) ? (object)DBNull.Value : availableDate);

                        await insertCmd.ExecuteNonQueryAsync();
                        _logger.LogInformation("Availability inserted successfully.");
                    }
                }

                return RedirectToPage("/setAvailability");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error setting availability: " + ex.Message);
                return RedirectToPage("/dashboardProfessor");
            }
        }

        public List<AvailabilityRecord> AvailabilityList { get; set; } = new();

        public class AvailabilityRecord
        {
            public string Status { get; set; }
            public string AvailableDate { get; set; }
            public string UnavailableDate { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public int? Capacity { get; set; }  // Add this property to hold the capacity value
        }


        public async Task LoadAvailabilityAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            using SqlConnection conn = new(_connString);
            await conn.OpenAsync();

            string getProfIdQuery = "SELECT professor_id FROM dbo.userCICT WHERE email = @Email";
            int professorId;
            using (SqlCommand cmd = new(getProfIdQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                var result = await cmd.ExecuteScalarAsync();
                if (result == null) return;
                professorId = (int)result;
            }

            string query = @"
    SELECT status, available_date, unavailable_date, start_time, end_time, capacity
    FROM dbo.professor_availability
    WHERE professor_id = @ProfessorId
    AND (
        (available_date IS NULL OR available_date >= CAST(GETDATE() AS DATE)) AND
        (unavailable_date IS NULL OR unavailable_date >= CAST(GETDATE() AS DATE))
    )
    ORDER BY COALESCE(available_date, unavailable_date)";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@ProfessorId", professorId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    AvailabilityList.Add(new AvailabilityRecord
                    {
                        Status = reader["status"].ToString(),
                        AvailableDate = reader["available_date"] == DBNull.Value ? null : Convert.ToDateTime(reader["available_date"]).ToString("yyyy-MM-dd"),
                        UnavailableDate = reader["unavailable_date"] == DBNull.Value ? null : Convert.ToDateTime(reader["unavailable_date"]).ToString("yyyy-MM-dd"),
                        StartTime = reader["start_time"] == DBNull.Value ? "N/A" : reader["start_time"].ToString(),
                        EndTime = reader["end_time"] == DBNull.Value ? "N/A" : reader["end_time"].ToString(),
                        Capacity = reader["capacity"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["capacity"])
                    });
                }
            }
        }

        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGetAsync() started");
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            using SqlConnection conn = new(_connString);
            await conn.OpenAsync();

            // Get professor ID first
            int professorId;
            string getProfIdQuery = "SELECT professor_id FROM dbo.userCICT WHERE email = @Email";
            using (SqlCommand cmd = new(getProfIdQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                var result = await cmd.ExecuteScalarAsync();
                if (result == null)
                {
                    _logger.LogWarning("Professor not found");
                    return;
                }
                professorId = (int)result;
            }

            await CleanUpPastAvailabilities(professorId);

            // Get user details
            string query = "SELECT first_name, last_name, role FROM dbo.userCICT WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                    _logger.LogInformation($"Retrieved Name: {FirstName} {LastName}, Role: {Role}");
                }
            }

            // Get profile picture
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                    _logger.LogInformation("Profile picture retrieved successfully.");
                }
                else
                {
                    _logger.LogWarning("No profile picture found.");
                }
            }

            await LoadAvailabilityAsync();
            
        }

        public async Task<IActionResult> OnPostDeleteAvailabilityAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return RedirectToPage("/Login");

            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                string getProfIdQuery = "SELECT professor_id FROM dbo.userCICT WHERE email = @Email";
                int professorId;
                using (SqlCommand cmd = new(getProfIdQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null) return RedirectToPage("/dashboardProfessor");
                    professorId = (int)result;
                }

                // Retrieve form values
                string status = Request.Form["Status"];
                string availableDate = Request.Form["AvailableDate"];
                string unavailableDate = Request.Form["UnavailableDate"];
                string startTime = Request.Form["StartTime"];
                string endTime = Request.Form["EndTime"];

                string deleteQuery = @"
            DELETE FROM dbo.professor_availability
            WHERE professor_id = @ProfessorId
            AND status = @Status
            AND (@AvailableDate IS NULL OR available_date = @AvailableDate)
            AND (@UnavailableDate IS NULL OR unavailable_date = @UnavailableDate)
            AND (@StartTime IS NULL OR start_time = @StartTime)
            AND (@EndTime IS NULL OR end_time = @EndTime)";

                using (SqlCommand cmd = new(deleteQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@ProfessorId", professorId);
                    cmd.Parameters.AddWithValue("@Status", status);

                    // Use DBNull if fields are null or "N/A"
                    cmd.Parameters.AddWithValue("@AvailableDate", string.IsNullOrEmpty(availableDate) || availableDate == "N/A" ? DBNull.Value : Convert.ToDateTime(availableDate));
                    cmd.Parameters.AddWithValue("@UnavailableDate", string.IsNullOrEmpty(unavailableDate) || unavailableDate == "N/A" ? DBNull.Value : Convert.ToDateTime(unavailableDate));
                    cmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTime) || startTime == "N/A" ? DBNull.Value : TimeSpan.Parse(startTime));
                    cmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTime) || endTime == "N/A" ? DBNull.Value : TimeSpan.Parse(endTime));

                    await cmd.ExecuteNonQueryAsync();
                }

                // Clean up past availabilities after any delete
                await CleanUpPastAvailabilities(professorId);

                return RedirectToPage("/setAvailability");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error deleting availability: " + ex.Message);
                return RedirectToPage("/setAvailability");
            }


        }


        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            byte[]? profileImage = null;
            using SqlConnection conn = new(_connString);
            conn.Open();

            string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    profileImage = (byte[])reader["Picture"];
                }
            }

            if (profileImage != null)
            {
                Response.Headers["Content-Disposition"] = "inline; filename=profile.jpg";
                return File(profileImage, "image/jpeg");
            }

            return File("/images/image.png", "image/png");
        }

        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}

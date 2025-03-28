using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;

namespace WebBookingApp.Pages
{
    public class setAvailabilitymodel : PageModel
    {
        private readonly ILogger<setAvailabilitymodel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public byte[]? ProfilePicture { get; set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; set; } = string.Empty;

        public setAvailabilitymodel(ILogger<setAvailabilitymodel> logger) => _logger = logger;

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

            if (string.IsNullOrEmpty(status))
            {
                _logger.LogWarning("Status not selected");
                return RedirectToPage("/setAvailability");
            }

            // ✅ New Validation: Check if both dates are empty
            if (string.IsNullOrEmpty(unavailableDate) && string.IsNullOrEmpty(availableDate))
            {
                _logger.LogWarning("Both Available and Unavailable dates are empty");
                return RedirectToPage("/setAvailability");
            }

            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

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

                string targetDate = !string.IsNullOrEmpty(availableDate) ? availableDate : unavailableDate;

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
                    string updateQuery = @"
                UPDATE dbo.professor_availability
                SET status = @Status, start_time = @StartTime, end_time = @EndTime,
                    unavailable_date = @UnavailableDate, available_date = @AvailableDate
                WHERE availability_id = @AvailabilityId";

                    using (SqlCommand updateCmd = new(updateQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@Status", status);
                        updateCmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTime) ? (object)DBNull.Value : startTime);
                        updateCmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTime) ? (object)DBNull.Value : endTime);
                        updateCmd.Parameters.AddWithValue("@UnavailableDate", string.IsNullOrEmpty(unavailableDate) ? (object)DBNull.Value : unavailableDate);
                        updateCmd.Parameters.AddWithValue("@AvailableDate", string.IsNullOrEmpty(availableDate) ? (object)DBNull.Value : availableDate);
                        updateCmd.Parameters.AddWithValue("@AvailabilityId", existingAvailabilityId.Value);

                        await updateCmd.ExecuteNonQueryAsync();
                        _logger.LogInformation("Availability updated successfully.");
                    }
                }
                else
                {
                    string insertQuery = @"
                INSERT INTO dbo.professor_availability 
                (professor_id, status, start_time, end_time, unavailable_date, available_date)
                VALUES (@ProfessorId, @Status, @StartTime, @EndTime, @UnavailableDate, @AvailableDate)";

                    using (SqlCommand insertCmd = new(insertQuery, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@ProfessorId", professorId);
                        insertCmd.Parameters.AddWithValue("@Status", status);
                        insertCmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTime) ? (object)DBNull.Value : startTime);
                        insertCmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTime) ? (object)DBNull.Value : endTime);
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
        }

        public async Task LoadAvailabilityAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            using SqlConnection conn = new(connString);
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
                SELECT status, available_date, unavailable_date, start_time, end_time
                FROM dbo.professor_availability
                WHERE professor_id = @ProfessorId
                ORDER BY available_date, unavailable_date";

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
                        EndTime = reader["end_time"] == DBNull.Value ? "N/A" : reader["end_time"].ToString()
                    });
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAvailabilityAsync(string Status, string AvailableDate, string UnavailableDate, string StartTime, string EndTime)
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            using SqlConnection conn = new(connString);
            await conn.OpenAsync();

            // Get the professor ID
            string getProfIdQuery = "SELECT professor_id FROM dbo.userCICT WHERE email = @Email";
            int professorId;
            using (SqlCommand cmd = new(getProfIdQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                var result = await cmd.ExecuteScalarAsync();
                if (result == null) return Page();
                professorId = (int)result;
            }

            string deleteQuery = @"
DELETE FROM dbo.professor_availability
WHERE status = @Status 
AND (available_date = @AvailableDate OR unavailable_date = @UnavailableDate)";

            using (SqlCommand deleteCmd = new(deleteQuery, conn))
            {
                deleteCmd.Parameters.AddWithValue("@Status", Status);

                deleteCmd.Parameters.AddWithValue("@AvailableDate",
                    string.IsNullOrEmpty(AvailableDate) ? (object)DBNull.Value : DateTime.Parse(AvailableDate));

                deleteCmd.Parameters.AddWithValue("@UnavailableDate",
                    string.IsNullOrEmpty(UnavailableDate) ? (object)DBNull.Value : DateTime.Parse(UnavailableDate));

                await deleteCmd.ExecuteNonQueryAsync();
            }


            return RedirectToPage(); // Refresh the page after deletion
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

            using SqlConnection conn = new(connString);
            await conn.OpenAsync();

            string query = "SELECT first_name, last_name FROM dbo.userCICT WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    _logger.LogInformation($"Retrieved Name: {FirstName} {LastName}");
                }
            }

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

        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            byte[]? profileImage = null;
            using SqlConnection conn = new(connString);
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

        public async Task<IActionResult> OnPostUploadPictureAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                Message = "Please select a file.";
                _logger.LogWarning("No file uploaded.");
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
                _logger.LogWarning("Invalid file format: " + fileExtension);
                return Page();
            }

            try
            {
                byte[] fileData;
                using (var memoryStream = new MemoryStream())
                {
                    await UploadedFile.CopyToAsync(memoryStream);
                    fileData = memoryStream.ToArray();
                }

                string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
                if (string.IsNullOrEmpty(userEmail))
                {
                    Message = "User not authenticated.";
                    return Page();
                }

                bool isProfessor = userEmail.EndsWith("@cict.edu");

                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                string query = @"
                    MERGE INTO dbo.userPictures AS target
                    USING (SELECT @Email AS Email) AS source
                    ON target.email = source.Email
                    WHEN MATCHED THEN 
                        UPDATE SET Picture = @ImageData, isProfessor = @IsProfessor
                    WHEN NOT MATCHED THEN 
                        INSERT (email, Picture, isProfessor) VALUES (@Email, @ImageData, @IsProfessor);";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    cmd.Parameters.AddWithValue("@ImageData", fileData);
                    cmd.Parameters.AddWithValue("@IsProfessor", isProfessor);
                    await cmd.ExecuteNonQueryAsync();
                }

                Message = "Profile picture updated successfully.";
                return RedirectToPage("/setAvailability");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error uploading picture: " + ex.Message);
                Message = "Error uploading picture.";
                return Page();
            }
        }


        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}
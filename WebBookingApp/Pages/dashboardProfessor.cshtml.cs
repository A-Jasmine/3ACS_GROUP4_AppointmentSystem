using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;

namespace WebBookingApp.Pages
{
    public class dashboardProfessorModel : PageModel
    {
        private readonly ILogger<dashboardProfessorModel> _logger;
        private readonly string _connString;

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public byte[]? ProfilePicture { get; set; }
        public string Role { get; set; } = "";
        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; set; } = string.Empty;

        public List<AppointmentHistory> RecentAppointments { get; set; } = new();  // Add this line to hold appointment history

        public class AppointmentHistory
        {
            public int AppointmentId { get; set; }
            public string BookerName { get; set; } = "";
            public string BookerType { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string FormattedDate => AppointmentDate.ToString("MMM dd, yyyy");
            public string FormattedTime => $"{AppointmentTime:hh\\:mm}";
        }

        public dashboardProfessorModel(ILogger<dashboardProfessorModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            // Fetch connection string dynamically from appsettings.json
            _connString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(_connString))
            {
                _logger.LogError("The connection string 'DefaultConnection' is missing in the configuration.");
                throw new InvalidOperationException("The ConnectionString property has not been initialized.");
            }
        }

        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGet() started");
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            // Define tasks for fetching data
            var fetchNameAndRoleTask = Task.Run(async () =>
            {
                try
                {
                    using SqlConnection conn = new(_connString);
                    await conn.OpenAsync();

                    string query = "SELECT first_name, last_name, role FROM dbo.userCICT WHERE email = @Email";
                    using SqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read())
                    {
                        FirstName = reader["first_name"].ToString();
                        LastName = reader["last_name"].ToString();
                        Role = reader["role"].ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching name and role: {ex.Message}");
                }
            });

            var fetchProfilePictureTask = Task.Run(async () =>
            {
                try
                {
                    using SqlConnection conn = new(_connString);
                    await conn.OpenAsync();

                    string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
                    using SqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read() && reader["Picture"] != DBNull.Value)
                    {
                        ProfilePicture = (byte[])reader["Picture"];
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching profile picture: {ex.Message}");
                }
            });

            // Fetch the recent appointment history
            var fetchAppointmentHistoryTask = Task.Run(async () =>
            {
                try
                {
                    using SqlConnection conn = new(_connString);
                    await conn.OpenAsync();

                    string query = @"
                        SELECT TOP 5
                            a.appointment_id,
                            COALESCE(s.first_name + ' ' + s.last_name, al.first_name + ' ' + al.last_name) AS booker_name,
                            CASE WHEN a.student_id IS NOT NULL THEN 'Student' ELSE 'Alumni' END AS booker_type,
                            a.appointment_date,
                            a.appointment_time,
                            a.purpose,
                            a.status
                        FROM dbo.Appointments a
                        LEFT JOIN dbo.userStudents s ON a.student_id = s.student_id
                        LEFT JOIN dbo.userAlumni al ON a.alumni_id = al.alumni_id
                        WHERE a.professor_id = (SELECT professor_id FROM dbo.userCICT WHERE email = @Email)
                        ORDER BY a.appointment_date DESC, a.appointment_time DESC";

                    using SqlCommand cmd = new(query, conn);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                        {
                            RecentAppointments.Add(new AppointmentHistory
                            {
                                AppointmentId = reader.GetInt32(0),
                                BookerName = reader["booker_name"].ToString(),
                                BookerType = reader["booker_type"].ToString(),
                                AppointmentDate = reader.GetDateTime(3),
                                AppointmentTime = reader.GetTimeSpan(4),
                                Purpose = reader["purpose"].ToString(),
                                Status = reader["status"].ToString()
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error fetching appointment history: {ex.Message}");
                }
            });

            // Run tasks in parallel
            await Task.WhenAll(fetchNameAndRoleTask, fetchProfilePictureTask, fetchAppointmentHistoryTask);

            _logger.LogInformation("Data fetched successfully.");
        }

        // [Keep all existing methods for profile picture, logout, etc.]
        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            byte[]? profileImage = null;

            using SqlConnection conn = new(_connString);
            conn.Open();

            // ✅ Fetch Profile Picture from dbo.userPictures
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

            return File("/images/image.png", "image/png"); // Fallback image
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

                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                // ✅ Check if user is a professor
                bool isProfessor = false;
                string checkProfessorQuery = "SELECT COUNT(*) FROM dbo.userCICT WHERE email = @Email";
                using (SqlCommand checkCmd = new(checkProfessorQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", userEmail);
                    isProfessor = (int)await checkCmd.ExecuteScalarAsync() > 0;
                }

                // ✅ Update or Insert Profile Picture and isProfessor flag
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

                _logger.LogInformation("Profile picture uploaded successfully.");
                return RedirectToPage("/dashboardProfessor");
            }
            catch (Exception ex)
            {
                Message = "Error uploading file: " + ex.Message;
                _logger.LogError("Error uploading profile picture: " + ex.Message);
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

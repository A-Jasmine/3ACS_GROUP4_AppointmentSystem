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
    public class AppointmentListModel : PageModel
    {
        private readonly ILogger<AppointmentListModel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public byte[]? ProfilePicture { get; set; }
        public string Role { get; set; } = "";
        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; set; } = "";
        public List<ProfessorAppointment> Appointments { get; set; } = new();

        public class ProfessorAppointment
        {
            public int AppointmentId { get; set; }
            public string BookerName { get; set; } = "";
            public string BookerType { get; set; } = ""; // Student or Alumni
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string Purpose { get; set; } = "";
            public string Mode { get; set; } = "";
            public string AdditionalNotes { get; set; } = "";
            public string Status { get; set; } = "";
            public string FormattedTime => $"{AppointmentTime:hh\\:mm}";
        }

        public AppointmentListModel(ILogger<AppointmentListModel> logger) => _logger = logger;

        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGet() started");
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            using SqlConnection conn = new(connString);
            await conn.OpenAsync();

            // Get professor details
            string query = "SELECT first_name, last_name, role FROM dbo.userCICT WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                }
            }

            // Get profile picture
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                }
            }

            // Get all appointments for this professor
            query = @"
                SELECT 
                    a.appointment_id,
                    COALESCE(s.first_name + ' ' + s.last_name, al.first_name + ' ' + al.last_name) AS booker_name,
                    CASE WHEN a.student_id IS NOT NULL THEN 'Student' ELSE 'Alumni' END AS booker_type,
                    a.appointment_date,
                    a.appointment_time,
                    a.purpose,
                    a.Mode,
                    a.additionalnotes,
                    a.status
                FROM dbo.Appointments a
                LEFT JOIN dbo.userStudents s ON a.student_id = s.student_id
                LEFT JOIN dbo.userAlumni al ON a.alumni_id = al.alumni_id
                WHERE a.professor_id = (SELECT professor_id FROM dbo.userCICT WHERE email = @Email)
                ORDER BY a.appointment_date DESC, a.appointment_time DESC";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (reader.Read())
                    {
                        Appointments.Add(new ProfessorAppointment
                        {
                            AppointmentId = reader.GetInt32(0),
                            BookerName = reader["booker_name"].ToString(),
                            BookerType = reader["booker_type"].ToString(),
                            AppointmentDate = reader.GetDateTime(3),
                            AppointmentTime = reader.GetTimeSpan(4),
                            Purpose = reader["purpose"].ToString(),
                            Mode = reader["mode"].ToString(),
                            AdditionalNotes = reader["additionalnotes"].ToString(),
                            Status = reader["status"].ToString()
                        });
                    }
                }
            }
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

            return profileImage != null
                ? File(profileImage, "image/jpeg", "profile.jpg")
                : File("/images/image.png", "image/png");
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

                using SqlConnection conn = new(connString);
                await conn.OpenAsync();
                string query = @"
                MERGE INTO dbo.userPictures AS target
                USING (SELECT @Email AS Email) AS source
                ON target.email = source.Email
                WHEN MATCHED THEN 
                    UPDATE SET Picture = @ImageData
                WHEN NOT MATCHED THEN 
                    INSERT (email, Picture) VALUES (@Email, @ImageData);";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    cmd.Parameters.AddWithValue("@ImageData", fileData);
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

        public async Task<IActionResult> OnPostApprove(int id)
        {
            return await UpdateAppointmentStatus(id, "Approved");
        }

        public async Task<IActionResult> OnPostDecline(int id)
        {
            return await UpdateAppointmentStatus(id, "Declined");
        }

        public async Task<IActionResult> OnPostComplete(int id)
        {
            return await UpdateAppointmentStatus(id, "Completed");
        }

        private async Task<IActionResult> UpdateAppointmentStatus(int id, string status)
        {
            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();
                string query = "UPDATE dbo.Appointments SET status = @Status WHERE appointment_id = @Id";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@Status", status);
                    await cmd.ExecuteNonQueryAsync();
                }
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating appointment status: {ex.Message}");
                Message = "Error updating appointment status";
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
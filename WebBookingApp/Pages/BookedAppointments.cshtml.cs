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
using System;
using System.Linq;

namespace WebBookingApp.Pages
{
    public class BookedAppointmentsModel : PageModel
    {
        private readonly ILogger<BookedAppointmentsModel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public List<AppointmentViewModel> Appointments { get; private set; } = new();

        public BookedAppointmentsModel(ILogger<BookedAppointmentsModel> logger) => _logger = logger;

        private static string FormatTime(TimeSpan time)
        {
            return DateTime.Today.Add(time).ToString("hh:mm tt"); // 12-hour format with AM/PM
        }

        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            using SqlConnection conn = new(connString);
            conn.Open();

            string query = @"
                SELECT first_name, last_name 
                FROM dbo.userStudents 
                WHERE email = @Email 
                UNION 
                SELECT first_name, last_name 
                FROM dbo.userAlumni 
                WHERE email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
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
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                    _logger.LogInformation("Profile picture retrieved successfully.");
                }
                else
                {
                    _logger.LogWarning("No profile picture found.");
                }
            }

            _logger.LogInformation("Retrieving booked appointments...");

            query = @"
    SELECT a.appointment_date, a.appointment_time, a.purpose, a.status,
           a.mode, a.additionalnotes,  -- ✅ Added Mode & Additional Notes
           p.first_name AS professor_first_name, p.last_name AS professor_last_name,
           up.Picture AS professor_picture
    FROM dbo.Appointments a
    INNER JOIN dbo.userCICT p ON a.professor_id = p.professor_id
    LEFT JOIN dbo.userPictures up ON p.email = up.email
    WHERE a.student_id = (SELECT student_id FROM dbo.userStudents WHERE email = @Email)
       OR a.alumni_id = (SELECT alumni_id FROM dbo.userAlumni WHERE email = @Email)
    ORDER BY a.appointment_date, a.appointment_time";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    byte[]? professorPicture = reader["professor_picture"] as byte[];
                    string professorPictureUrl = professorPicture != null
                        ? $"data:image/jpeg;base64,{Convert.ToBase64String(professorPicture)}"
                        : "/images/image.png";

                    Appointments.Add(new AppointmentViewModel
                    {
                        ProfessorName = $"{reader["professor_first_name"]} {reader["professor_last_name"]}",
                        ProfessorPicture = professorPictureUrl,
                        AppointmentDate = reader.GetDateTime(reader.GetOrdinal("appointment_date")),
                        AppointmentTime = reader.GetTimeSpan(reader.GetOrdinal("appointment_time")),
                        Purpose = reader["purpose"].ToString(),
                        Status = reader["status"].ToString(),
                        Mode = reader["mode"].ToString(),  // ✅ Store Mode
                        AdditionalNotes = reader["additionalnotes"].ToString()  // ✅ Store Additional Notes
                    });
                }
            }


            _logger.LogInformation($"Total appointments fetched: {Appointments.Count}");
        }

        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            using SqlConnection conn = new(connString);
            conn.Open();

            byte[]? profileImage = null;
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

                return RedirectToPage("/dashboardStudent");
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

        public class AppointmentViewModel
        {
            public string ProfessorName { get; set; } = "";
            public string ProfessorPicture { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string FormattedAppointmentTime => FormatTime(AppointmentTime);
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string Mode { get; set; } = "";  // ✅ New Field
            public string AdditionalNotes { get; set; } = "";  // ✅ New Field
        }
    }
}

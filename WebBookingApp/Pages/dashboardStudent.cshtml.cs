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

namespace WebBookingApp.Pages
{
    public class dashboardStudentModel : PageModel
    {
        private readonly ILogger<dashboardStudentModel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public class Appointment
        {
            public string ProfessorName { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string ProfessorProfilePicture { get; set; } = "";
            public string FormattedAppointmentTime => DateTime.Today.Add(AppointmentTime).ToString("hh:mm tt");
            public string Mode { get; set; } = "";  // ✅ New Field
            public string AdditionalNotes { get; set; } = "";  // ✅ New Field
        }

        public List<Appointment> Appointments { get; private set; } = new();

        public dashboardStudentModel(ILogger<dashboardStudentModel> logger) => _logger = logger;

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

            // Retrieve user details
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

            // Retrieve profile picture
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
            }

            // Get student or alumni ID
            string? studentId = null, alumniId = null;
            query = @"
                SELECT CAST(student_id AS NVARCHAR) AS id, 'student' AS role 
                FROM dbo.userStudents 
                WHERE email = @Email 
                UNION 
                SELECT CAST(alumni_id AS NVARCHAR) AS id, 'alumni' AS role 
                FROM dbo.userAlumni 
                WHERE email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    string role = reader["role"].ToString();
                    string id = reader["id"]?.ToString();

                    if (role == "student") studentId = id;
                    else if (role == "alumni") alumniId = id;
                }
            }

            // ✅ Retrieve appointment history including Mode and Additional Notes
            query = @"
                SELECT 
                    p.first_name + ' ' + p.last_name AS professor_name,
                    a.appointment_date,
                    a.appointment_time,
                    a.purpose,
                    a.status,
                    a.mode,              -- ✅ Added Mode field
                    a.additionalnotes,  -- ✅ Added Additional Notes field
                    up.Picture AS professor_picture
                FROM dbo.Appointments a
                JOIN dbo.userCICT p ON a.professor_id = p.professor_id
                LEFT JOIN dbo.userPictures up ON p.email = up.email
                WHERE (@StudentID IS NULL OR a.student_id = @StudentID)
                  AND (@AlumniID IS NULL OR a.alumni_id = @AlumniID)
                ORDER BY a.appointment_date DESC, a.appointment_time DESC";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@StudentID", (object?)studentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AlumniID", (object?)alumniId ?? DBNull.Value);

                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    byte[]? professorPicture = reader["professor_picture"] as byte[];
                    string base64ProfessorPicture = professorPicture != null
                        ? $"data:image/jpeg;base64,{Convert.ToBase64String(professorPicture)}"
                        : "/images/image.png";

                    Appointments.Add(new Appointment
                    {
                        ProfessorName = reader["professor_name"].ToString(),
                        AppointmentDate = Convert.ToDateTime(reader["appointment_date"]),
                        AppointmentTime = (TimeSpan)reader["appointment_time"],
                        Purpose = reader["purpose"].ToString(),
                        Status = reader["status"].ToString(),
                        Mode = reader["mode"]?.ToString() ?? "N/A",  // ✅ Fetch Mode
                        AdditionalNotes = reader["additionalnotes"]?.ToString() ?? "None", // ✅ Fetch Additional Notes
                        ProfessorProfilePicture = base64ProfessorPicture
                    });
                }
            }
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
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();
            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
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
                if (string.IsNullOrEmpty(userEmail)) return Page();

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

                return RedirectToPage("/dashboardStudent");
            }
            catch
            {
                Message = "Error uploading file.";
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

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using static WebBookingApp.Pages.dashboardStudentModel;

namespace WebBookingApp.Pages
{
    public class BookedAppointmentsModel : PageModel
    {
        private readonly ILogger<BookedAppointmentsModel> _logger;
        private readonly string _connString;

        public BookedAppointmentsModel(ILogger<BookedAppointmentsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connString = configuration.GetConnectionString("DefaultConnection");
        }

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public string Role { get; set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public string StudentOrganizationName { get; private set; } = "N/A";
        public string StudentOrgRole { get; private set; } = "N/A";

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public List<FacultyAppointment> FacultyAppointments { get; private set; } = new();
        public List<OrganizationAppointment> OrganizationAppointments { get; private set; } = new();
        public List<AlumniAppointment> AlumniAppointments { get; private set; } = new();


        private static string FormatTime(TimeSpan time)
        {
            return DateTime.Today.Add(time).ToString("hh:mm tt");
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

            using SqlConnection conn = new(_connString);
            conn.Open();
            string query = @"
            SELECT 
                us.first_name, 
                us.last_name, 
                us.role, 
                org.OrganizationName, 
                om.Role AS OrgRole
            FROM dbo.userStudents us
            LEFT JOIN dbo.OrganizationMembers om ON us.student_id = om.StudentId
            LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
            WHERE us.email = @Email

            UNION

            SELECT 
                ua.first_name, 
                ua.last_name, 
                ua.role, 
                NULL AS OrganizationName, 
                NULL AS OrgRole
            FROM dbo.userAlumni ua
            WHERE ua.email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                    StudentOrganizationName = reader["OrganizationName"]?.ToString() ?? "N/A";
                    StudentOrgRole = reader["OrgRole"]?.ToString() ?? "N/A";

                    _logger.LogInformation($"Name: {FirstName} {LastName}, Role: {Role}, Org: {StudentOrganizationName}, Org Role: {StudentOrgRole}");
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

            // 1. Get Faculty Appointments
            query = @"
        SELECT 
            p.first_name + ' ' + p.last_name AS professor_name,
            a.appointment_date,
            a.appointment_time,
            a.purpose,
            a.status,
            a.mode,
            a.additionalnotes,
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

                    FacultyAppointments.Add(new FacultyAppointment
                    {
                        ProfessorName = reader["professor_name"].ToString(),
                        AppointmentDate = Convert.ToDateTime(reader["appointment_date"]),
                        AppointmentTime = (TimeSpan)reader["appointment_time"],
                        Purpose = reader["purpose"].ToString(),
                        Status = reader["status"].ToString(),
                        Mode = reader["mode"]?.ToString() ?? "N/A",
                        AdditionalNotes = reader["additionalnotes"]?.ToString() ?? "None",
                        ProfessorProfilePicture = base64ProfessorPicture
                    });
                }
            }

            // 2. Get Alumni Appointments (fixed version)
            if (studentId != null || alumniId != null)
            {
                query = @"
            SELECT 
                a.first_name + ' ' + a.last_name AS alumni_name,
                sa.AppointmentDate,
                sa.AppointmentTime,
                sa.Purpose,
                sa.Status,
                sa.Mode,
                sa.AdditionalNotes,
                up.Picture AS alumni_picture
            FROM dbo.StudentAlumniAppointments sa
            JOIN dbo.userAlumni a ON 
                (sa.AlumniID = a.alumni_id AND sa.StudentID = @StudentID) OR
                (sa.StudentID = a.alumni_id AND sa.AlumniID = @AlumniID)
            LEFT JOIN dbo.userPictures up ON a.email = up.email
            WHERE 
                (@StudentID IS NOT NULL AND sa.StudentID = @StudentID) OR
                (@AlumniID IS NOT NULL AND sa.AlumniID = @AlumniID)
            ORDER BY sa.AppointmentDate DESC, sa.AppointmentTime DESC";

                _logger.LogInformation($"Fetching alumni appointments for StudentID: {studentId}, AlumniID: {alumniId}");

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentID", (object?)studentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AlumniID", (object?)alumniId ?? DBNull.Value);

                    using SqlDataReader reader = cmd.ExecuteReader();
                    int count = 0;
                    while (reader.Read())
                    {
                        count++;
                        byte[]? alumniPicture = reader["alumni_picture"] as byte[];
                        string base64AlumniPicture = alumniPicture != null
                            ? $"data:image/jpeg;base64,{Convert.ToBase64String(alumniPicture)}"
                            : "/images/image.png";

                        AlumniAppointments.Add(new AlumniAppointment
                        {
                            AlumniName = reader["alumni_name"].ToString(),
                            AppointmentDate = Convert.ToDateTime(reader["AppointmentDate"]),
                            AppointmentTime = (TimeSpan)reader["AppointmentTime"],
                            Purpose = reader["Purpose"].ToString(),
                            Status = reader["Status"].ToString(),
                            Mode = reader["Mode"]?.ToString() ?? "N/A",
                            AdditionalNotes = reader["AdditionalNotes"]?.ToString() ?? "None",
                            AlumniProfilePicture = base64AlumniPicture
                        });
                    }
                    _logger.LogInformation($"Total alumni appointments found: {count}");
                }
            }

            // 3. Get Organization Appointments
            if (studentId != null || alumniId != null)
            {
                query = @"
            SELECT 
                s.first_name + ' ' + s.last_name AS org_member_name,
                so.AppointmentDate,
                so.AppointmentTime,
                so.Purpose,
                so.Status,
                so.Mode,
                so.AdditionalNotes,
                up.Picture AS org_member_picture
            FROM dbo.StudentOrganizationAppointments so
            JOIN dbo.OrganizationMembers om ON so.OrganizationMemberID = om.StudentId
            JOIN dbo.userStudents s ON om.StudentId = s.student_id
            LEFT JOIN dbo.userPictures up ON s.email = up.email
            WHERE (@StudentID IS NULL OR so.StudentID = @StudentID)
              AND (@AlumniID IS NULL OR so.AlumniID = @AlumniID)
            ORDER BY so.AppointmentDate DESC, so.AppointmentTime DESC";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentID", (object?)studentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AlumniID", (object?)alumniId ?? DBNull.Value);

                    using SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        byte[]? orgPicture = reader["org_member_picture"] as byte[];
                        string base64OrgPicture = orgPicture != null
                            ? $"data:image/jpeg;base64,{Convert.ToBase64String(orgPicture)}"
                            : "/images/image.png";

                        OrganizationAppointments.Add(new OrganizationAppointment
                        {
                            OrganizationMemberName = reader["org_member_name"].ToString(),
                            AppointmentDate = Convert.ToDateTime(reader["AppointmentDate"]),
                            AppointmentTime = (TimeSpan)reader["AppointmentTime"],
                            Purpose = reader["Purpose"].ToString(),
                            Status = reader["Status"].ToString(),
                            Mode = reader["Mode"]?.ToString() ?? "N/A",
                            AdditionalNotes = reader["AdditionalNotes"]?.ToString() ?? "None",
                            OrganizationMemberProfilePicture = base64OrgPicture
                        });
                    }
                }
            }
        }


        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            using SqlConnection conn = new(_connString);
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

                using SqlConnection conn = new(_connString);
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


        public class FacultyAppointment
        {
            public string ProfessorName { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string FormattedAppointmentTime => DateTime.Today.Add(AppointmentTime).ToString("hh:mm tt");
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string ProfessorProfilePicture { get; set; } = "";
            public string Mode { get; set; } = "";
            public string AdditionalNotes { get; set; } = "";
        }

        public class AlumniAppointment
        {
            public string AlumniName { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string FormattedAppointmentTime => DateTime.Today.Add(AppointmentTime).ToString("hh:mm tt");
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string AlumniProfilePicture { get; set; } = "";
            public string Mode { get; set; } = "";
            public string AdditionalNotes { get; set; } = "";
        }

        public class OrganizationAppointment
        {
            public string OrganizationMemberName { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string FormattedAppointmentTime => DateTime.Today.Add(AppointmentTime).ToString("hh:mm tt");
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string OrganizationMemberProfilePicture { get; set; } = "";
            public string Mode { get; set; } = "";
            public string AdditionalNotes { get; set; } = "";
        }
        public class AppointmentViewModel
        {
            public int Id { get; set; }
            public string ProfessorName { get; set; } = "";
            public string ProfessorPicture { get; set; } = "";
            public DateTime AppointmentDate { get; set; }
            public TimeSpan AppointmentTime { get; set; }
            public string FormattedAppointmentTime => FormatTime(AppointmentTime);
            public string Purpose { get; set; } = "";
            public string Status { get; set; } = "";
            public string Mode { get; set; } = "";  // New Field
            public string AdditionalNotes { get; set; } = "";  // New Field
        }
    }
}

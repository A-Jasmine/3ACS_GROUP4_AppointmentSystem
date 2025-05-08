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
    public class SetAvailabilityStudentOrgModel : PageModel
    {
        private readonly ILogger<SetAvailabilityStudentOrgModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connString;

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public string Role { get; set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public string StudentOrganizationName { get; private set; } = "N/A";
        public string StudentOrgRole { get; private set; } = "N/A";
        public List<StudentOrgAvailability> AvailabilityList { get; private set; } = new();

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public class StudentOrgAvailability
        {
            public int AvailabilityID { get; set; }
            public string StudentId { get; set; }
            public int OrganizationID { get; set; }
            public string Status { get; set; }
            public DateTime? AvailableDate { get; set; }
            public TimeSpan? StartTime { get; set; }
            public TimeSpan? EndTime { get; set; }
            public int? MaxCapacity { get; set; }
        }

        public SetAvailabilityStudentOrgModel(
         ILogger<SetAvailabilityStudentOrgModel> logger,
         IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connString = _configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> OnPostAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return Page();

            using SqlConnection conn = new(_connString);
            await conn.OpenAsync();

            string studentId = "";
            int organizationId = 0;

            string query = @"
                SELECT s.student_id, om.OrganizationID
                FROM dbo.userStudents s
                LEFT JOIN dbo.OrganizationMembers om ON s.student_id = om.StudentId
                WHERE s.email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    studentId = reader["student_id"].ToString();
                    organizationId = Convert.ToInt32(reader["OrganizationID"]);
                }
            }

            if (string.IsNullOrEmpty(studentId) || organizationId == 0) return Page();

            var status = Request.Form["status"].ToString();
            var availableDateStr = Request.Form["available_date"].ToString();
            var startTimeStr = Request.Form["start_time"].ToString();
            var endTimeStr = Request.Form["end_time"].ToString();
            var capacityStr = Request.Form["capacity"].ToString();

            string insertQuery = @"
            INSERT INTO dbo.OrganizationAvailability
            (StudentId, OrganizationID, Status, AvailableDate, StartTime, EndTime, MaxCapacity)
            VALUES
            (@StudentId, @OrganizationID, @Status, @AvailableDate, @StartTime, @EndTime, @MaxCapacity)";


            using (SqlCommand cmd = new(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                cmd.Parameters.AddWithValue("@OrganizationID", organizationId);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@AvailableDate", string.IsNullOrEmpty(availableDateStr) ? DBNull.Value : DateTime.Parse(availableDateStr));
                cmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTimeStr) ? DBNull.Value : TimeSpan.Parse(startTimeStr));
                cmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTimeStr) ? DBNull.Value : TimeSpan.Parse(endTimeStr));
                cmd.Parameters.AddWithValue("@MaxCapacity", string.IsNullOrEmpty(capacityStr) ? DBNull.Value : int.Parse(capacityStr));

                await cmd.ExecuteNonQueryAsync();
            }

            return RedirectToPage();
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
                SELECT us.first_name, us.last_name, us.role, us.student_id, org.OrganizationID, org.OrganizationName, om.Role AS OrgRole

                FROM dbo.userStudents us
                LEFT JOIN dbo.OrganizationMembers om ON us.student_id = om.StudentId
                LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
                WHERE us.email = @Email";

            string studentId = "";
            int organizationId = 0;

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

                    studentId = reader["student_id"]?.ToString();
                    organizationId = Convert.ToInt32(reader["OrganizationID"]);
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
                }
            }

            if (!string.IsNullOrEmpty(studentId))
            {
                query = @"
                    SELECT AvailabilityID, StudentId, OrganizationID, Status, AvailableDate, StartTime, EndTime, MaxCapacity
                    FROM dbo.OrganizationAvailability
                    WHERE StudentId = @StudentId
                    ORDER BY AvailableDate DESC";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    using SqlDataReader reader = cmd.ExecuteReader();
                    AvailabilityList = new List<StudentOrgAvailability>();
                    while (reader.Read())
                    {
                        AvailabilityList.Add(new StudentOrgAvailability
                        {
                            AvailabilityID = Convert.ToInt32(reader["AvailabilityID"]),
                            StudentId = reader["StudentId"].ToString(),
                            OrganizationID = Convert.ToInt32(reader["OrganizationID"]),
                            Status = reader["Status"].ToString(),
                            AvailableDate = reader["AvailableDate"] as DateTime?,
                            StartTime = reader["StartTime"] as TimeSpan?,
                            EndTime = reader["EndTime"] as TimeSpan?,
                            MaxCapacity = reader["MaxCapacity"] as int?
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

                return RedirectToPage("/dashboardStudent");
            }
            catch
            {
                Message = "Error uploading file.";
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAvailabilityAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return RedirectToPage("/Login");

            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                // Retrieve studentId and organizationId based on the logged-in user's email
                string studentId = "";
                int organizationId = 0;

                string getStudentOrgQuery = @"
            SELECT s.student_id, om.OrganizationID
            FROM dbo.userStudents s
            LEFT JOIN dbo.OrganizationMembers om ON s.student_id = om.StudentId
            WHERE s.email = @Email";

                using (SqlCommand cmd = new(getStudentOrgQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            studentId = reader["student_id"].ToString();
                            organizationId = Convert.ToInt32(reader["OrganizationID"]);
                        }
                    }
                }

                // If studentId or organizationId is not found, return to the dashboard
                if (string.IsNullOrEmpty(studentId) || organizationId == 0)
                {
                    return RedirectToPage("/dashboardStudent");
                }

                // Retrieve form values (AvailabilityID to delete)
                int availabilityId = Convert.ToInt32(Request.Form["AvailabilityID"]);

                // SQL query to delete the availability record based on the AvailabilityID
                string deleteQuery = @"
            DELETE FROM dbo.OrganizationAvailability
            WHERE AvailabilityID = @AvailabilityID
            AND StudentId = @StudentId
            AND OrganizationID = @OrganizationID";

                using (SqlCommand cmd = new(deleteQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@AvailabilityID", availabilityId);
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    cmd.Parameters.AddWithValue("@OrganizationID", organizationId);

                    await cmd.ExecuteNonQueryAsync();
                }

                // After successful deletion, redirect to the same page
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                // Log any error that occurred during the deletion process
                _logger.LogError("Error deleting availability: " + ex.Message);
                return RedirectToPage("/setAvailability");
            }
        }


        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}

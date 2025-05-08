using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

namespace WebBookingApp.Pages
{
    public class editStudentProfileModel : PageModel
    {
        private readonly ILogger<editStudentProfileModel> _logger;
        private readonly string _connString;

        public class Student
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string MiddleName { get; set; }
            public string Email { get; set; }
            public string MobileNumber { get; set; }
            public string Program { get; set; }
            public string Role { get; set; }
            public string StudentOrg { get; set; }
            public string StudentId { get; set; }
            public string YearLevel { get; set; }
            public DateTime? JoinedAt { get; set; }
        }

        [BindProperty]
        public Student SelectedStudent { get; set; }

        [BindProperty] public string StudentOrg { get; set; } = "";
        [BindProperty] public string RoleFromOrg { get; set; } = "";
        [BindProperty] public IFormFile? UploadedFile { get; set; }

        public string Message { get; private set; } = "";

        public byte[]? ProfilePicture { get; private set; }

        // Inject IConfiguration to retrieve configuration settings
        public editStudentProfileModel(ILogger<editStudentProfileModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            // Retrieve the connection string from appsettings.json
            _connString = configuration.GetConnectionString("DefaultConnection");
        }

        public void OnGet(string id)
        {
            _logger.LogInformation($"OnGet started. studentId: {id}");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("Student ID is null or empty.");
                return;
            }

            using SqlConnection conn = new(_connString);
            conn.Open();

            string query = @"
        SELECT first_name, middle_name, last_name, email, mobile_number, program, role, student_org, student_id, YearLevel
        FROM dbo.userStudents 
        WHERE student_id = @StudentId";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.Add("@StudentId", SqlDbType.NVarChar, 50).Value = id;

                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    SelectedStudent = new Student
                    {
                        StudentId = reader["student_id"].ToString(),
                        FirstName = reader["first_name"].ToString(),
                        MiddleName = reader["middle_name"]?.ToString(),
                        LastName = reader["last_name"].ToString(),
                        Email = reader["email"].ToString(),
                        MobileNumber = reader["mobile_number"].ToString(),
                        Program = reader["program"].ToString(),
                        Role = reader["role"].ToString(),
                        StudentOrg = reader["student_org"]?.ToString(),
                        YearLevel = reader["YearLevel"]?.ToString()
                    };
                    _logger.LogInformation($"Student data fetched: {SelectedStudent.FirstName} {SelectedStudent.LastName}");
                }
                else
                {
                    _logger.LogWarning("No student data found for the provided ID.");
                    return;
                }
            }

            // 🔥 Load org role and joined date
            string membershipQuery = @"
        SELECT OM.Role, SO.OrganizationName, OM.JoinedAt
        FROM dbo.OrganizationMembers OM
        JOIN dbo.StudentOrganizations SO ON OM.OrganizationID = SO.OrganizationID
        WHERE OM.StudentId = @StudentId";

            using (SqlCommand cmd = new(membershipQuery, conn))
            {
                cmd.Parameters.AddWithValue("@StudentId", id);

                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    RoleFromOrg = reader["Role"]?.ToString();
                    StudentOrg = reader["OrganizationName"]?.ToString();
                    SelectedStudent.JoinedAt = reader["JoinedAt"] != DBNull.Value ? (DateTime?)reader.GetDateTime(reader.GetOrdinal("JoinedAt")) : null;
                }
            }
        }
        public async Task<IActionResult> OnPostSaveAsync()
        {
            if (SelectedStudent == null || string.IsNullOrEmpty(SelectedStudent.StudentId))
            {
                _logger.LogError("SelectedStudent is null or StudentId is missing.");
                Message = "Student data is not available for editing.";
                return Page();
            }

            try
            {
                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                // Determine the role - prioritize the base role (Student/Alumni) over organization role
                string role = SelectedStudent.Role;
                if (string.IsNullOrEmpty(role))
                {
                    role = string.IsNullOrEmpty(RoleFromOrg) ? "Student" : RoleFromOrg;
                }
                _logger.LogInformation($"Final role being saved: {role}");

                using SqlTransaction transaction = conn.BeginTransaction();

                try
                {
                    // Update userStudents
                    string updateStudentQuery = @"
                UPDATE dbo.userStudents
                SET first_name = @FirstName, 
                    middle_name = @MiddleName, 
                    last_name = @LastName, 
                    mobile_number = @MobileNumber,
                    program = @Program, 
                    role = @Role,
                    student_org = @StudentOrg, 
                    YearLevel = @YearLevel
                WHERE student_id = @StudentId";

                    using (SqlCommand cmd = new(updateStudentQuery, conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                        cmd.Parameters.AddWithValue("@FirstName", SelectedStudent.FirstName);
                        cmd.Parameters.AddWithValue("@MiddleName", string.IsNullOrEmpty(SelectedStudent.MiddleName) ? (object)DBNull.Value : SelectedStudent.MiddleName);
                        cmd.Parameters.AddWithValue("@LastName", SelectedStudent.LastName);
                        cmd.Parameters.AddWithValue("@MobileNumber", string.IsNullOrEmpty(SelectedStudent.MobileNumber) ? (object)DBNull.Value : SelectedStudent.MobileNumber);
                        cmd.Parameters.AddWithValue("@Program", SelectedStudent.Program);
                        cmd.Parameters.AddWithValue("@Role", role);
                        cmd.Parameters.AddWithValue("@StudentOrg", string.IsNullOrEmpty(StudentOrg) || StudentOrg == "None" ? (object)DBNull.Value : StudentOrg);
                        cmd.Parameters.AddWithValue("@YearLevel", SelectedStudent.YearLevel ?? (object)DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    _logger.LogInformation("Student profile updated.");

                    // If the student is now an alumni, move their data
                    if (role == "Alumni")
                    {
                        _logger.LogInformation("Role is Alumni. Moving data to dbo.userAlumni.");

                        // Insert into userAlumni and copy the password from userStudents
                        string insertToAlumniQuery = @"
    INSERT INTO dbo.userAlumni (alumni_id, first_name, middle_name, last_name, email, mobile_number, program, role, password)
    SELECT student_id, first_name, middle_name, last_name, email, mobile_number, program, role, password
    FROM dbo.userStudents
    WHERE student_id = @StudentId";


                        using (SqlCommand cmd = new(insertToAlumniQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Delete from userStudents
                        string deleteFromStudentsQuery = "DELETE FROM dbo.userStudents WHERE student_id = @StudentId";

                        using (SqlCommand cmd = new(deleteFromStudentsQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        _logger.LogInformation("Student moved to alumni and deleted from userStudents.");

                        transaction.Commit();
                        Message = "Student has been moved to Alumni.";
                        return RedirectToPage("/manageStudents");
                    }

                    // Handle organization membership (only if still a student)
                    if (string.IsNullOrEmpty(StudentOrg) || StudentOrg == "None")
                    {
                        string deleteMembershipQuery = "DELETE FROM dbo.OrganizationMembers WHERE StudentId = @StudentId";
                        using (SqlCommand cmd = new(deleteMembershipQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        string clearOrgQuery = "UPDATE dbo.userStudents SET student_org = NULL WHERE student_id = @StudentId";
                        using (SqlCommand cmd = new(clearOrgQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        _logger.LogInformation("Organization membership cleared.");
                    }
                    else
                    {
                        // Ensure org exists
                        string checkOrgQuery = @"
                    SELECT COUNT(*) FROM dbo.StudentOrganizations 
                    WHERE LTRIM(RTRIM(OrganizationName)) COLLATE SQL_Latin1_General_CP1_CI_AS = @StudentOrg";
                        int orgCount;
                        using (SqlCommand cmd = new(checkOrgQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentOrg", StudentOrg.Trim());
                            orgCount = (int)await cmd.ExecuteScalarAsync();
                        }

                        if (orgCount == 0)
                        {
                            string insertOrgQuery = "INSERT INTO dbo.StudentOrganizations (OrganizationName) VALUES (@StudentOrg)";
                            using (SqlCommand cmd = new(insertOrgQuery, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@StudentOrg", StudentOrg.Trim());
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string getOrgIdQuery = @"
                    SELECT OrganizationID FROM dbo.StudentOrganizations 
                    WHERE LTRIM(RTRIM(OrganizationName)) COLLATE SQL_Latin1_General_CP1_CI_AS = @StudentOrg";

                        int orgId;
                        using (SqlCommand cmd = new(getOrgIdQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentOrg", StudentOrg.Trim());
                            orgId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        }

                        string removeMembershipsQuery = "DELETE FROM dbo.OrganizationMembers WHERE StudentId = @StudentId";
                        using (SqlCommand cmd = new(removeMembershipsQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        string orgRole = !string.IsNullOrEmpty(RoleFromOrg) ? RoleFromOrg : "Member";
                        string insertMemberQuery = @"
                    INSERT INTO dbo.OrganizationMembers (StudentId, OrganizationID, Role, JoinedAt)
                    VALUES (@StudentId, @OrganizationID, @Role, @JoinedAt)";

                        using (SqlCommand cmd = new(insertMemberQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", SelectedStudent.StudentId);
                            cmd.Parameters.AddWithValue("@OrganizationID", orgId);
                            cmd.Parameters.AddWithValue("@Role", orgRole);
                            cmd.Parameters.AddWithValue("@JoinedAt", SelectedStudent.JoinedAt ?? DateTime.Today);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        _logger.LogInformation("Student organization membership updated.");
                    }

                    transaction.Commit();
                    Message = "Student profile updated successfully.";
                    return RedirectToPage("/manageStudents");
                }
                catch (Exception innerEx)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError($"Transaction failed: {innerEx.Message}");
                    Message = "Error processing student update.";
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Database connection failed: {ex.Message}");
                Message = "Error updating profile.";
                return Page();
            }
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
    }
}

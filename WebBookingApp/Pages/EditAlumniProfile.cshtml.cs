using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System;
using System.ComponentModel.DataAnnotations;
using Dapper;

namespace WebBookingApp.Pages
{
    public class EditAlumniProfileModel : PageModel
    {
        private readonly ILogger<EditAlumniProfileModel> _logger;
        private readonly string _connectionString;

        public AlumniFacultyViewModel SelectedPerson { get; set; } = new();
        public byte[]? ProfilePicture { get; private set; }
        public string Message { get; private set; } = "";

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Id { get; set; }

        public EditAlumniProfileModel(ILogger<EditAlumniProfileModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task OnGetAsync()
        {
            _logger.LogInformation("OnGetAsync called. ID value: {Id}", Id);
            _logger.LogInformation("Query string: {QueryString}", Request.QueryString);

            if (string.IsNullOrEmpty(Id))
            {
                Message = "Alumni ID not provided";
                _logger.LogWarning("Alumni ID not provided. Available values: " +
                                 $"Route: {Request.RouteValues["id"]}, " +
                                 $"Query: {Request.Query["id"]}");
                return;
            }


            using var connection = new SqlConnection(_connectionString);

            // Load basic alumni info
            var alumni = await connection.QueryFirstOrDefaultAsync<AlumniFacultyViewModel>(
                @"SELECT 
                    alumni_id AS Id,
                    first_name AS FirstName,
                    last_name AS LastName,
                    middle_name AS MiddleName,
                    email AS Email,
                    mobile_number AS MobileNumber,
                    program AS Program,
                    COALESCE(role, 'Alumni') AS CurrentRole
                FROM dbo.userAlumni
                WHERE alumni_id = @AlumniId",
                new { AlumniId = Id });

            if (alumni == null)
            {
                Message = "Alumni not found";
                return;
            }

            SelectedPerson = alumni;

            // If alumni is already faculty, load additional info
            if (SelectedPerson.CurrentRole != "Alumni")
            {
                var facultyInfo = await connection.QueryFirstOrDefaultAsync(
                    @"SELECT 
                        gender AS Gender,
                        date_of_birth AS DateOfBirth,
                        address AS Address,
                        city AS City,
                        employment_status AS EmploymentStatus,
                        joining_date AS JoiningDate,
                        organization_adviser AS OrganizationAdviser
                    FROM dbo.userCICT
                    WHERE email = @Email",
                    new { SelectedPerson.Email });

                if (facultyInfo != null)
                {
                    SelectedPerson.Gender = facultyInfo.Gender;
                    SelectedPerson.DateOfBirth = facultyInfo.DateOfBirth;
                    SelectedPerson.Address = facultyInfo.Address;
                    SelectedPerson.City = facultyInfo.City;
                    SelectedPerson.EmploymentStatus = facultyInfo.EmploymentStatus;
                    SelectedPerson.JoiningDate = facultyInfo.JoiningDate;
                    SelectedPerson.OrganizationAdviser = facultyInfo.OrganizationAdviser ?? "None";
                }
            }


            // With this corrected code:
            ProfilePicture = (await connection.QueryAsync<byte[]>(
                "SELECT Picture FROM dbo.userPictures WHERE email = @Email",
                new { SelectedPerson.Email })).FirstOrDefault();
                
            
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            if (!ModelState.IsValid)
            {
                Message = "Please correct the errors below";
                return Page();
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Update alumni record
                await connection.ExecuteAsync(
                    @"UPDATE dbo.userAlumni 
                    SET 
                        first_name = @FirstName,
                        last_name = @LastName,
                        middle_name = @MiddleName,
                        mobile_number = @MobileNumber,
                        program = @Program,
                        role = @CurrentRole
                    WHERE alumni_id = @Id",
                    SelectedPerson, transaction);

                // Handle faculty conversion
                if (SelectedPerson.CurrentRole != "Alumni")
                {
                    // Check if faculty record exists
                    var facultyExists = await connection.ExecuteScalarAsync<bool>(
                        @"SELECT CASE WHEN EXISTS (
                            SELECT 1 FROM dbo.userCICT 
                            WHERE email = @Email
                        ) THEN 1 ELSE 0 END",
                        new { SelectedPerson.Email }, transaction);

                    if (facultyExists)
                    {
                        // Update existing faculty record
                        await connection.ExecuteAsync(
                            @"UPDATE dbo.userCICT 
                            SET 
                                first_name = @FirstName,
                                last_name = @LastName,
                                middle_name = @MiddleName,
                                program = @Program,
                                role = @CurrentRole,
                                contact_number = @MobileNumber,
                                gender = @Gender,
                                date_of_birth = @DateOfBirth,
                                address = @Address,
                                city = @City,
                                employment_status = @EmploymentStatus,
                                joining_date = @JoiningDate
                            WHERE email = @Email",
                            SelectedPerson, transaction);
                    }
                    else
                    {
                        // Insert new faculty record
                        await connection.ExecuteAsync(
                            @"INSERT INTO dbo.userCICT (
                                email, password, last_name, first_name, program, role,
                                middle_name, contact_number, gender, date_of_birth,
                                address, city, joining_date, employment_status,
                                organization_adviser
                            )
                            VALUES (
                                @Email, NULL, @LastName, @FirstName, @Program, @CurrentRole,
                                @MiddleName, @MobileNumber, @Gender, @DateOfBirth,
                                @Address, @City, @JoiningDate, @EmploymentStatus,
                                CASE WHEN @OrganizationAdviser = 'None' THEN NULL ELSE @OrganizationAdviser END
                            )",
                            SelectedPerson, transaction);
                    }
                }
                else
                {
                    // Remove faculty record if exists
                    await connection.ExecuteAsync(
                        "DELETE FROM dbo.userCICT WHERE email = @Email",
                        new { SelectedPerson.Email }, transaction);
                }

                await transaction.CommitAsync();
                Message = "Profile updated successfully!";
                return RedirectToPage(new { id = Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Message = $"Error updating profile: {ex.Message}";
                _logger.LogError(ex, "Error updating alumni profile");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostPromoteToFacultyAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Get alumni data
                var alumni = await connection.QueryFirstOrDefaultAsync<AlumniFacultyViewModel>(
                    @"SELECT 
                        alumni_id AS Id,
                        first_name AS FirstName,
                        last_name AS LastName,
                        middle_name AS MiddleName,
                        email AS Email,
                        mobile_number AS MobileNumber,
                        program AS Program
                    FROM dbo.userAlumni
                    WHERE alumni_id = @AlumniId",
                    new { AlumniId = Id });

                if (alumni == null)
                {
                    Message = "Alumni not found";
                    return Page();
                }

                // Set default faculty values
                alumni.CurrentRole = "Instructor";
                alumni.EmploymentStatus = "Full-time";
                alumni.JoiningDate = DateTime.Today;

                // Insert into faculty table
                await connection.ExecuteAsync(
                    @"INSERT INTO dbo.userCICT (
                        email, password, last_name, first_name, program, role,
                        middle_name, contact_number, employment_status, joining_date
                    )
                    VALUES (
                        @Email, NULL, @LastName, @FirstName, @Program, @CurrentRole,
                        @MiddleName, @MobileNumber, @EmploymentStatus, @JoiningDate
                    )",
                    alumni);

                // Update alumni role
                await connection.ExecuteAsync(
                    "UPDATE dbo.userAlumni SET role = @Role WHERE alumni_id = @Id",
                    new { Role = alumni.CurrentRole, Id });

                Message = "Alumni successfully promoted to faculty!";
                return RedirectToPage(new { id = Id });
            }
            catch (Exception ex)
            {
                Message = $"Error promoting to faculty: {ex.Message}";
                _logger.LogError(ex, "Error promoting alumni to faculty");
                return Page();
            }
        }

        public async Task<IActionResult> OnGetProfilePictureAsync()
        {
            var email = SelectedPerson?.Email ?? User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email)) return NotFound();

            using var connection = new SqlConnection(_connectionString);
            var imageData = await connection.QueryFirstOrDefaultAsync<byte[]>(
                "SELECT Picture FROM dbo.userPictures WHERE email = @Email",
                new { Email = email });

            return imageData != null
                ? File(imageData, "image/jpeg")
                : File("~/images/default-profile.png", "image/png");
        }

        public async Task<IActionResult> OnPostUploadPictureAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                Message = "Please select a file to upload";
                return Page();
            }

            try
            {
                using var memoryStream = new MemoryStream();
                await UploadedFile.CopyToAsync(memoryStream);
                var imageData = memoryStream.ToArray();

                var email = SelectedPerson?.Email ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(email)) return Page();

                using var connection = new SqlConnection(_connectionString);
                await connection.ExecuteAsync(
                    @"MERGE INTO dbo.userPictures AS target
                    USING (SELECT @Email AS Email) AS sourcze
                    ON target.email = source.Email
                    WHEN MATCHED THEN 
                        UPDATE SET Picture = @ImageData
                    WHEN NOT MATCHED THEN 
                        INSERT (email, Picture) VALUES (@Email, @ImageData);",
                    new { Email = email, ImageData = imageData });

                Message = "Profile picture updated successfully!";
                return RedirectToPage(new { id = Id });
            }
            catch (Exception ex)
            {
                Message = $"Error uploading picture: {ex.Message}";
                _logger.LogError(ex, "Error uploading profile picture");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }

        public class AlumniFacultyViewModel
        {
            public string Id { get; set; } = null!;

            [Required]
            public string FirstName { get; set; } = null!;

            [Required]
            public string LastName { get; set; } = null!;

            public string? MiddleName { get; set; }

            [Required, EmailAddress]
            public string Email { get; set; } = null!;

            [Required]
            public string MobileNumber { get; set; } = null!;

            [Required]
            public string Program { get; set; } = null!;

            [Required]
            public string CurrentRole { get; set; } = "Alumni";

            // Faculty-specific fields
            public string? Gender { get; set; }
            public DateTime? DateOfBirth { get; set; }
            public string? Address { get; set; }
            public string? City { get; set; }
            public string? EmploymentStatus { get; set; }
            public DateTime? JoiningDate { get; set; }
            public string? OrganizationAdviser { get; set; } = "None";
        }
    }
}
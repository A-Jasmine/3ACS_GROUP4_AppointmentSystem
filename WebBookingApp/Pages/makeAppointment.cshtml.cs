using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using static WebBookingApp.Pages.manageStudentsModel;

namespace WebBookingApp.Pages
{
    public class makeAppointmentModel : PageModel
    {
        public class Professor
        {
            public int ProfessorId { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public byte[]? Picture { get; set; }
        }

        public List<string> UnavailableDates { get; private set; } = new();
        public List<Professor> Professors { get; private set; } = new();

        private readonly ILogger<makeAppointmentModel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public string Message { get; private set; } = "";

        [BindProperty] public string AdditionalNotes { get; set; } = "";
        [BindProperty] public IFormFile? UploadedFile { get; set; }
        [BindProperty] public int SelectedProfessorId { get; set; }
        [BindProperty] public DateTime? AppointmentDate { get; set; }
        [BindProperty] public TimeSpan AppointmentTime { get; set; }
        [BindProperty] public string Purpose { get; set; } = "";
        [BindProperty] public string Mode { get; set; } = "";
        [BindProperty] public string YearSection { get; set; } = "";



        public makeAppointmentModel(ILogger<makeAppointmentModel> logger) => _logger = logger;

        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");
            string? userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            using SqlConnection conn = new(connString);
            conn.Open();

            try
            {
                #region Fetch Student/Alumni Name
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
                        _logger.LogInformation($"User: {FirstName} {LastName}");
                    }
                }
                #endregion

                #region Fetch Profile Picture
                query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = cmd.ExecuteReader();
                    if (reader.Read() && reader["Picture"] != DBNull.Value)
                    {
                        ProfilePicture = (byte[])reader["Picture"];
                        _logger.LogInformation("Profile picture loaded.");
                    }
                    else _logger.LogInformation("No profile picture found.");
                }
                #endregion

                #region Fetch Professors
                string professorQuery = @"
                    SELECT c.professor_id, c.first_name, c.last_name, p.Picture
                    FROM dbo.userCICT c
                    LEFT JOIN dbo.userPictures p 
                    ON c.email = p.email AND p.isProfessor = 1";

                using (SqlCommand cmd = new(professorQuery, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Professors.Add(new Professor
                        {
                            ProfessorId = Convert.ToInt32(reader["professor_id"]),
                            FirstName = reader["first_name"].ToString(),
                            LastName = reader["last_name"].ToString(),
                            Picture = reader["Picture"] != DBNull.Value ? (byte[])reader["Picture"] : null
                        });
                    }
                }
                #endregion
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in OnGet(): {ex.Message}");
            }
        }

        public JsonResult OnGetUnavailableDates(int professorId)
        {
            _logger.LogInformation($"Fetching unavailable dates for professor_id: {professorId}");
            List<string> unavailableDates = new();

            try
            {
                using SqlConnection conn = new(connString);
                conn.Open();
                string query = @"
                    SELECT DISTINCT CONVERT(varchar, unavailable_date, 23) AS unavailable_date
                    FROM dbo.professor_availability
                    WHERE status = 'Unavailable' AND professor_id = @ProfessorId";

                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@ProfessorId", professorId);
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    unavailableDates.Add(reader["unavailable_date"].ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching unavailable dates: {ex.Message}");
            }

            return new JsonResult(unavailableDates);
        }

        public async Task<IActionResult> OnPostBookAppointmentAsync()
        {
            string? userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                Message = "User not authenticated.";
                return Page();
            }

            string studentId = "";
            string alumniId = "";

            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                // Check if the user is a student or alumni
                string checkQuery = "SELECT student_id FROM dbo.userStudents WHERE email = @Email";
                using (SqlCommand cmd = new(checkQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null) studentId = result.ToString();
                }

                if (string.IsNullOrEmpty(studentId))
                {
                    // Check if alumni
                    checkQuery = "SELECT alumni_id FROM dbo.userAlumni WHERE email = @Email";
                    using SqlCommand cmd = new(checkQuery, conn);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null) alumniId = result.ToString();
                }

                // Insert the appointment
                string insertQuery = @"
                INSERT INTO dbo.Appointments (student_id, alumni_id, professor_id, appointment_date, appointment_time, purpose, additionalnotes, Mode, Year_Section)
                VALUES (@StudentId, @AlumniId, @ProfessorId, @AppointmentDate, @AppointmentTime, @Purpose, @AdditionalNotes, @Mode, @YearSection);";

                using (SqlCommand cmd = new(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", string.IsNullOrEmpty(studentId) ? DBNull.Value : (object)studentId);
                    cmd.Parameters.AddWithValue("@AlumniId", string.IsNullOrEmpty(alumniId) ? DBNull.Value : (object)alumniId);
                    cmd.Parameters.AddWithValue("@ProfessorId", SelectedProfessorId);
                    cmd.Parameters.AddWithValue("@AppointmentDate", AppointmentDate);
                    cmd.Parameters.AddWithValue("@AppointmentTime", AppointmentTime);
                    cmd.Parameters.AddWithValue("@Purpose", Purpose);
                    cmd.Parameters.AddWithValue("@AdditionalNotes", string.IsNullOrEmpty(AdditionalNotes) ? DBNull.Value : (object)AdditionalNotes);
                    cmd.Parameters.AddWithValue("@Mode", Mode);
                    cmd.Parameters.AddWithValue("@YearSection", YearSection);

                    await cmd.ExecuteNonQueryAsync();
                }


                Message = "Appointment booked successfully!";
                _logger.LogInformation("Appointment booked.");
                return RedirectToPage("/makeAppointment"); // Redirect or change this as needed
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error booking appointment: {ex.Message}");
                Message = "Error booking appointment.";
                return Page();
            }
        }

        public IActionResult OnGetProfilePicture()
        {
            string? userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            try
            {
                using SqlConnection conn = new(connString);
                conn.Open();

                string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    byte[] image = (byte[])reader["Picture"];
                    return File(image, "image/jpeg", "profile.jpg");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching profile picture: {ex.Message}");
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

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
            string fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
                _logger.LogWarning($"Invalid file extension: {fileExtension}");
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

                string? userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
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

                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Email", userEmail);
                cmd.Parameters.AddWithValue("@ImageData", fileData);
                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Profile picture uploaded successfully.");
                return RedirectToPage("/makeAppointment");
            }
            catch (Exception ex)
            {
                Message = $"Error uploading file: {ex.Message}";
                _logger.LogError($"Upload error: {ex.Message}");
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

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;
using static WebBookingApp.Pages.manageStudentsModel;

namespace WebBookingApp.Pages
{
    public class makeAppointmentModel : PageModel
    {
        public class TimeSlot
        {
            public DateTime Date { get; set; }
            public string TimeRange { get; set; }
            public TimeSpan StartTime { get; set; }
            public TimeSpan EndTime { get; set; }

            // Method to split into 1-hour intervals
            public List<TimeSlot> SplitIntoOneHourSlots()
            {
                var slots = new List<TimeSlot>();
                var currentStart = StartTime;

                while (currentStart < EndTime)
                {
                    var currentEnd = currentStart.Add(TimeSpan.FromHours(1));
                    if (currentEnd > EndTime)
                    {
                        currentEnd = EndTime;
                    }

                    slots.Add(new TimeSlot
                    {
                        Date = Date,
                        StartTime = currentStart,
                        EndTime = currentEnd,
                        TimeRange = $"{currentStart:hh\\:mm} - {currentEnd:hh\\:mm}"
                    });

                    currentStart = currentEnd;
                }

                return slots;
            }
        }

        public class ProfessorViewModel
        {
            public int ProfessorId { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string Role { get; set; }
            public byte[]? Picture { get; set; }
            public bool IsAvailable { get; set; }
        }

        public class Professor
        {
            public int ProfessorId { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public byte[]? Picture { get; set; }
            public string Role { get; set; } = string.Empty;
            public bool IsAvailable { get; set; }
            public string AvailabilityStatus { get; set; } = "Unavailable";
            public List<TimeSlot> AvailableSlots { get; set; } = new();
            public bool HasSpecificDates { get; set; }
            public int Capacity
            {
                get => _capacity;
                set => _capacity = Math.Max(1, value); // Ensure capacity is at least 1
            }
            private int _capacity = 1;

            public bool GetIsAvailable()
            {
                // If no availability records exist at all, mark as unavailable
                if (AvailableSlots == null || !AvailableSlots.Any())
                    return false;

                // If specific dates are set but none are in the future
                if (HasSpecificDates && !AvailableSlots.Any(s => s.Date >= DateTime.Today))
                    return false;

                return true;
            }

            public string GetAvailabilityStatus()
            {
                // Default to Unavailable if no availability is set
                if (AvailableSlots == null || !AvailableSlots.Any())
                    return "Unavailable";

                if (!GetIsAvailable())
                    return "Unavailable";

                if (HasSpecificDates && AvailableSlots.Any())
                {
                    var nextAvailableDate = AvailableSlots.Where(s => s.Date >= DateTime.Today)
                                                        .Select(s => s.Date)
                                                        .Min();
                    return $"Available until {nextAvailableDate:MMM dd}";
                }

                return "Available";
            }

            public List<TimeSlot> GetAvailableSlotsForDate(DateTime date)
            {
                return AvailableSlots
                    .Where(slot => slot.Date.Date == date.Date)
                    .OrderBy(slot => slot.StartTime)
                    .ToList();
            }
        }

        public class AlumniModel
        {
            public string AlumniId { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public byte[]? Picture { get; set; }
            public bool IsAvailable { get; set; }
            public string AvailabilityStatus { get; set; } = "Unavailable";
            public List<TimeSlot> AvailableSlots { get; set; } = new();
            public bool HasSpecificDates { get; set; }
            public int Capacity
            {
                get => _capacity;
                set => _capacity = Math.Max(1, value); // Ensure capacity is at least 1
            }
            private int _capacity = 1;

            public bool GetIsAvailable()
            {
                if (AvailableSlots == null || !AvailableSlots.Any())
                    return false;

                if (HasSpecificDates && !AvailableSlots.Any(s => s.Date >= DateTime.Today))
                    return false;

                return true;
            }

            public string GetAvailabilityStatus()
            {
                if (AvailableSlots == null || !AvailableSlots.Any())
                    return "Unavailable";

                if (!GetIsAvailable())
                    return "Unavailable";

                if (HasSpecificDates && AvailableSlots.Any())
                {
                    var nextAvailableDate = AvailableSlots.Where(s => s.Date >= DateTime.Today)
                                                        .Select(s => s.Date)
                                                        .Min();
                    return $"Available until {nextAvailableDate:MMM dd}";
                }

                return "Available";
            }

            public List<TimeSlot> GetAvailableSlotsForDate(DateTime date)
            {
                return AvailableSlots
                    .Where(slot => slot.Date.Date == date.Date)
                    .OrderBy(slot => slot.StartTime)
                    .ToList();
            }
        }



        public class OrganizationMember
        {
            public string StudentId { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            [BindProperty] public string OrgRole { get; set; } = string.Empty;
            public byte[]? Picture { get; set; }
            public string Organization { get; set; } = string.Empty;
            public int? OrganizationId { get; set; }
            public bool IsAvailable { get; set; }
            public string AvailabilityStatus { get; set; } = "Unavailable";
            public List<TimeSlot> AvailableSlots { get; set; } = new();
            public bool HasSpecificDates { get; set; }
            public int Capacity
            {
                get => _capacity;
                set => _capacity = Math.Max(1, value); // Ensure capacity is at least 1
            }
            private int _capacity = 1;


            public bool GetIsAvailable()
            {
                if (AvailableSlots == null || !AvailableSlots.Any())
                    return false;

                if (HasSpecificDates && !AvailableSlots.Any(s => s.Date >= DateTime.Today))
                    return false;

                return true;
            }

            public string GetAvailabilityStatus()
            {
                if (AvailableSlots == null || !AvailableSlots.Any())
                    return "Unavailable";

                if (!GetIsAvailable())
                    return "Unavailable";

                if (HasSpecificDates && AvailableSlots.Any())
                {
                    var nextAvailableDate = AvailableSlots.Where(s => s.Date >= DateTime.Today)
                                                        .Select(s => s.Date)
                                                        .Min();
                    return $"Available until {nextAvailableDate:MMM dd}";
                }

                return "Available";
            }

            public List<TimeSlot> GetAvailableSlotsForDate(DateTime date)
            {
                return AvailableSlots
                    .Where(slot => slot.Date.Date == date.Date)
                    .OrderBy(slot => slot.StartTime)
                    .ToList();
            }
        }

        public List<Professor> Professors { get; private set; } = new();
        public List<AlumniModel> Alumni { get; private set; } = new();
        public List<OrganizationMember> OrganizationMembers { get; private set; } = new();

        private readonly ILogger<makeAppointmentModel> _logger;
        private readonly string connString;

        public string FirstName { get; private set; } = string.Empty;
        public string LastName { get; private set; } = string.Empty;
        public string StudentOrganizationName { get; private set; } = "N/A";
        public string StudentOrgRole { get; private set; } = "N/A";

        public byte[]? ProfilePicture { get; private set; }
        public string Message { get; private set; } = string.Empty;

        [BindProperty] public string AdditionalNotes { get; set; } = string.Empty;
        [BindProperty] public IFormFile? UploadedFile { get; set; }
        [BindProperty] public int SelectedProfessorId { get; set; }
        [BindProperty] public DateTime? AppointmentDate { get; set; }
        [BindProperty] public TimeSpan AppointmentTime { get; set; }
        [BindProperty] public string Purpose { get; set; } = string.Empty;
        [BindProperty] public string Mode { get; set; } = string.Empty;
        [BindProperty] public string YearSection { get; set; } = string.Empty;
        [BindProperty] public string SelectedAlumniId { get; set; } = string.Empty;

        [BindProperty] public string Role { get; set; } = string.Empty;
        [BindProperty] public string SelectedOrganizationMemberId { get; set; } = string.Empty;
        [BindProperty] public string UserBaseRole { get; set; } = string.Empty;

        public makeAppointmentModel(ILogger<makeAppointmentModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            connString = configuration.GetConnectionString("DefaultConnection");
        }

        public void OnGet()
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

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
                _logger.LogInformation("Fetching user details.");
                string query = @"
SELECT 
    us.student_id,
    us.first_name, 
    us.last_name, 
    us.role as base_role,
    org.OrganizationName as StudentOrganizationName,
    om.Role AS StudentOrgRole,
    om.OrganizationID
FROM dbo.userStudents us
LEFT JOIN dbo.OrganizationMembers om ON us.student_id = om.StudentId
LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
WHERE us.email = @Email

UNION

SELECT 
    ua.alumni_id as student_id,
    ua.first_name, 
    ua.last_name, 
    ua.role as base_role,
    NULL AS StudentOrganizationName,
    NULL AS StudentOrgRole,
    NULL AS OrganizationID
FROM dbo.userAlumni ua
WHERE ua.email = @Email";

                string userOrganization = string.Empty;
                string userOrgRole = string.Empty;
                int? userOrganizationId = null;

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        FirstName = reader["first_name"]?.ToString() ?? string.Empty;
                        LastName = reader["last_name"]?.ToString() ?? string.Empty;
                        UserBaseRole = reader["base_role"]?.ToString() ?? string.Empty;
                        userOrganization = reader["StudentOrganizationName"]?.ToString() ?? string.Empty;
                        userOrgRole = reader["StudentOrgRole"]?.ToString() ?? string.Empty;
                        userOrganizationId = reader["OrganizationID"] as int?;

                        StudentOrganizationName = userOrganization;
                        StudentOrgRole = userOrgRole;
                    }
                    reader.Close();
                }

                // Fetch profile picture
                query = @"
SELECT p.Picture
FROM dbo.userPictures p
LEFT JOIN dbo.userStudents s ON p.email = s.email
LEFT JOIN dbo.userAlumni a ON p.email = a.email
WHERE s.email = @Email OR a.email = @Email";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    using SqlDataReader reader = cmd.ExecuteReader();
                    if (reader.Read() && reader["Picture"] != DBNull.Value)
                    {
                        ProfilePicture = (byte[])reader["Picture"];
                    }
                    else
                    {
                        var defaultImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "image.png");
                        ProfilePicture = System.IO.File.ReadAllBytes(defaultImagePath);
                    }
                    reader.Close();
                }

                // Modified professor query to include availability calculation
                string professorQuery = @"
SELECT 
    c.professor_id, 
    c.first_name, 
    c.last_name, 
    p.Picture, 
    c.role,
    pa.start_time AS available_start,
    pa.end_time AS available_end,
    pa.available_date,
    pa.status,
    pa.capacity
FROM dbo.userCICT c
LEFT JOIN dbo.userPictures p ON c.email = p.email
LEFT JOIN dbo.professor_availability pa ON c.professor_id = pa.professor_id
ORDER BY c.professor_id";

                var professorDict = new Dictionary<int, Professor>();

                using (SqlCommand cmd = new(professorQuery, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int professorId = Convert.ToInt32(reader["professor_id"]);

                        if (!professorDict.TryGetValue(professorId, out var professor))
                        {
                            professor = new Professor
                            {
                                ProfessorId = professorId,
                                FirstName = reader["first_name"]?.ToString() ?? string.Empty,
                                LastName = reader["last_name"]?.ToString() ?? string.Empty,
                                Picture = reader["Picture"] != DBNull.Value ? (byte[])reader["Picture"] : null,
                                Role = reader["role"]?.ToString() ?? string.Empty,
                                HasSpecificDates = false,
                                Capacity = reader["capacity"] != DBNull.Value ? Convert.ToInt32(reader["capacity"]) : 1
                            };
                            professorDict[professorId] = professor;
                        }

                        if (!reader.IsDBNull(reader.GetOrdinal("status")))
                        {
                            DateTime availableDate = !reader.IsDBNull(reader.GetOrdinal("available_date"))
                                ? reader.GetDateTime(reader.GetOrdinal("available_date"))
                                : DateTime.MinValue;

                            if (!reader.IsDBNull(reader.GetOrdinal("available_start")) &&
                                !reader.IsDBNull(reader.GetOrdinal("available_end")))
                            {
                                TimeSpan startTime = reader.GetTimeSpan(reader.GetOrdinal("available_start"));
                                TimeSpan endTime = reader.GetTimeSpan(reader.GetOrdinal("available_end"));

                                var baseSlot = new TimeSlot
                                {
                                    Date = availableDate,
                                    StartTime = startTime,
                                    EndTime = endTime,
                                    TimeRange = $"{startTime:hh\\:mm} - {endTime:hh\\:mm}"
                                };

                                // Use your existing SplitIntoOneHourSlots method
                                var oneHourSlots = baseSlot.SplitIntoOneHourSlots();

                                foreach (var slot in oneHourSlots)
                                {
                                    if (!professor.AvailableSlots.Any(s =>
                                        s.Date == slot.Date &&
                                        s.StartTime == slot.StartTime &&
                                        s.EndTime == slot.EndTime))
                                    {
                                        professor.AvailableSlots.Add(slot);
                                        if (availableDate != DateTime.MinValue)
                                        {
                                            professor.HasSpecificDates = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Update availability status for all professors
                foreach (var professor in professorDict.Values)
                {
                    professor.IsAvailable = professor.GetIsAvailable();
                    professor.AvailabilityStatus = professor.GetAvailabilityStatus();
                }

                Professors = professorDict.Values.ToList();

                // Only fetch alumni if current user is not an alumni
                if (UserBaseRole != "Alumni")
                {
                    _logger.LogInformation("Fetching alumni list (current user is not an alumni)");
                    string alumniQuery = @"
SELECT 
    a.alumni_id, 
    a.first_name, 
    a.last_name, 
    u.Picture,
    aa.start_time AS available_start,
    aa.end_time AS available_end,
    aa.available_date,
    aa.is_available,
    aa.capacity
FROM dbo.userAlumni a
LEFT JOIN dbo.userPictures u ON a.email = u.email
LEFT JOIN dbo.AlumniAvailability aa ON a.alumni_id = aa.alumni_id";

                    var alumniDict = new Dictionary<string, AlumniModel>();

                    using (SqlCommand cmd = new(alumniQuery, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string alumniId = reader["alumni_id"]?.ToString() ?? string.Empty;

                            if (!alumniDict.TryGetValue(alumniId, out var alumni))
                            {
                                alumni = new AlumniModel
                                {
                                    AlumniId = alumniId,
                                    FirstName = reader["first_name"]?.ToString() ?? string.Empty,
                                    LastName = reader["last_name"]?.ToString() ?? string.Empty,
                                    Picture = reader["Picture"] != DBNull.Value ? (byte[])reader["Picture"] :
                                        System.IO.File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "image.png")),
                                    Capacity = reader["capacity"] != DBNull.Value ? Convert.ToInt32(reader["capacity"]) : 1
                                };
                                alumniDict[alumniId] = alumni;
                            }

                            DateTime availableDate = !reader.IsDBNull(reader.GetOrdinal("available_date"))
                                ? reader.GetDateTime(reader.GetOrdinal("available_date"))
                                : DateTime.MinValue;

                            if (!reader.IsDBNull(reader.GetOrdinal("available_start")) &&
                                !reader.IsDBNull(reader.GetOrdinal("available_end")))
                            {
                                TimeSpan startTime = reader.GetTimeSpan(reader.GetOrdinal("available_start"));
                                TimeSpan endTime = reader.GetTimeSpan(reader.GetOrdinal("available_end"));

                                var baseSlot = new TimeSlot
                                {
                                    Date = availableDate,
                                    StartTime = startTime,
                                    EndTime = endTime,
                                    TimeRange = $"{startTime:hh\\:mm} - {endTime:hh\\:mm}"
                                };

                                var splitSlots = baseSlot.SplitIntoOneHourSlots();
                                foreach (var slot in splitSlots)
                                {
                                    if (!alumni.AvailableSlots.Any(s =>
                                        s.Date == slot.Date &&
                                        s.StartTime == slot.StartTime &&
                                        s.EndTime == slot.EndTime))
                                    {
                                        alumni.AvailableSlots.Add(slot);
                                        if (availableDate != DateTime.MinValue)
                                        {
                                            alumni.HasSpecificDates = true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Update availability status for all alumni
                    foreach (var alumni in alumniDict.Values)
                    {
                        alumni.IsAvailable = alumni.GetIsAvailable();
                        alumni.AvailabilityStatus = alumni.GetAvailabilityStatus();
                    }

                    Alumni = alumniDict.Values.ToList();
                }

                // Fetch organization members based on user's role and organization
                if (UserBaseRole == "Student")
                {
                    _logger.LogInformation($"Fetching organization members (UserOrg: {userOrganization}, OrgID: {userOrganizationId})");

                    string orgMemberQuery;
                    if (string.IsNullOrEmpty(userOrganization))
                    {
                        orgMemberQuery = @"
SELECT 
    om.StudentId, 
    u.first_name, 
    u.last_name, 
    om.Role, 
    p.Picture, 
    org.OrganizationName AS student_org,
    om.OrganizationID,
    oa.StartTime AS available_start,
    oa.EndTime AS available_end,
    oa.AvailableDate,
    oa.Status,
    oa.MaxCapacity
FROM dbo.OrganizationMembers om
JOIN dbo.userStudents u ON om.StudentId = u.student_id
LEFT JOIN dbo.userPictures p ON u.email = p.email
LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
LEFT JOIN dbo.OrganizationAvailability oa ON om.StudentId = oa.StudentId AND om.OrganizationID = oa.OrganizationID";
                    }
                    else
                    {
                        orgMemberQuery = @"
SELECT 
    om.StudentId, 
    u.first_name, 
    u.last_name, 
    om.Role, 
    p.Picture, 
    org.OrganizationName AS student_org,
    om.OrganizationID,
    oa.StartTime AS available_start,
    oa.EndTime AS available_end,
    oa.AvailableDate,
    oa.Status,
    oa.MaxCapacity
FROM dbo.OrganizationMembers om
JOIN dbo.userStudents u ON om.StudentId = u.student_id
LEFT JOIN dbo.userPictures p ON u.email = p.email
LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
LEFT JOIN dbo.OrganizationAvailability oa ON om.StudentId = oa.StudentId AND om.OrganizationID = oa.OrganizationID
WHERE om.OrganizationID = @OrganizationID 
AND om.StudentId != @CurrentUserId";
                    }

                    var orgMemberDict = new Dictionary<string, OrganizationMember>();

                    using (SqlCommand cmd = new(orgMemberQuery, conn))
                    {
                        if (!string.IsNullOrEmpty(userOrganization))
                        {
                            cmd.Parameters.AddWithValue("@OrganizationID", userOrganizationId ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId ?? (object)DBNull.Value);
                        }

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string studentId = reader["StudentId"]?.ToString() ?? string.Empty;

                                if (!orgMemberDict.TryGetValue(studentId, out var orgMember))
                                {
                                    orgMember = new OrganizationMember
                                    {
                                        StudentId = studentId,
                                        FirstName = reader["first_name"]?.ToString() ?? string.Empty,
                                        LastName = reader["last_name"]?.ToString() ?? string.Empty,
                                        OrgRole = reader["Role"]?.ToString() ?? string.Empty,
                                        Picture = reader["Picture"] != DBNull.Value ? (byte[])reader["Picture"] :
                                            System.IO.File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "image.png")),
                                        Organization = reader["student_org"]?.ToString() ?? "None",
                                        OrganizationId = reader["OrganizationID"] as int?,
                                        Capacity = reader["MaxCapacity"] != DBNull.Value ? Convert.ToInt32(reader["MaxCapacity"]) : 1
                                    };
                                    orgMemberDict[studentId] = orgMember;
                                }

                                DateTime availableDate = !reader.IsDBNull(reader.GetOrdinal("AvailableDate"))
                                    ? reader.GetDateTime(reader.GetOrdinal("AvailableDate"))
                                    : DateTime.MinValue;

                                if (!reader.IsDBNull(reader.GetOrdinal("available_start")) &&
                                    !reader.IsDBNull(reader.GetOrdinal("available_end")))
                                {
                                    TimeSpan startTime = reader.GetTimeSpan(reader.GetOrdinal("available_start"));
                                    TimeSpan endTime = reader.GetTimeSpan(reader.GetOrdinal("available_end"));

                                    // For organization members
                                    var baseSlot = new TimeSlot
                                    {
                                        Date = availableDate,
                                        StartTime = startTime,
                                        EndTime = endTime,
                                        TimeRange = $"{startTime:hh\\:mm} - {endTime:hh\\:mm}"
                                    };

                                    var splitSlots = baseSlot.SplitIntoOneHourSlots();
                                    foreach (var slot in splitSlots)
                                    {
                                        if (!orgMember.AvailableSlots.Any(s =>
                                            s.Date == slot.Date &&
                                            s.StartTime == slot.StartTime &&
                                            s.EndTime == slot.EndTime))
                                        {
                                            orgMember.AvailableSlots.Add(slot);
                                            if (availableDate != DateTime.MinValue)
                                            {
                                                orgMember.HasSpecificDates = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Update availability status for all organization members
                    foreach (var orgMember in orgMemberDict.Values)
                    {
                        orgMember.IsAvailable = orgMember.GetIsAvailable();
                        orgMember.AvailabilityStatus = orgMember.GetAvailabilityStatus();
                    }

                    OrganizationMembers = orgMemberDict.Values.ToList();
                    _logger.LogInformation($"Loaded {OrganizationMembers.Count} organization members");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in OnGet(): {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                conn.Close();
                _logger.LogInformation("Database connection closed");
            }
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            await HttpContext.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToPage("/Login");
        }

        public async Task<IActionResult> OnPostUploadPictureAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                Message = "Please select a file.";
                _logger.LogWarning("No file selected for upload.");
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
                    _logger.LogWarning("User not authenticated during file upload.");
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
                _logger.LogError($"Error uploading profile picture: {ex.Message}");
                return Page();
            }
        }
        public async Task<IActionResult> OnPostAsync()
        {
            string? userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["Error"] = "User not authenticated. Please log in again.";
                _logger.LogWarning("Booking failed: no user email found.");
                return RedirectToPage();
            }

            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();
                _logger.LogInformation("Database connection established.");

                // Get user ID based on their role (student or alumni)
                string userId = "";
                string idQuery = @"
SELECT COALESCE(
    (SELECT student_id FROM dbo.userStudents WHERE email = @Email),
    (SELECT alumni_id FROM dbo.userAlumni WHERE email = @Email)
) AS user_id";

                using (SqlCommand cmd = new(idQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        userId = result.ToString()!;
                        _logger.LogInformation($"Found user ID: {userId} for email: {userEmail}");
                    }
                    else
                    {
                        TempData["Error"] = "No user ID found for your account.";
                        _logger.LogError(TempData["Error"]?.ToString());
                        return RedirectToPage();
                    }
                }
                _logger.LogInformation("User ID retrieved: " + userId);

                bool bookingSuccess = false;

                // Handle different booking types with capacity checks
                if (SelectedProfessorId > 0)
                {
                    bookingSuccess = await BookProfessorAppointment(conn, userId);
                }
                else if (!string.IsNullOrEmpty(SelectedAlumniId))
                {
                    bookingSuccess = await BookAlumniAppointment(conn, userId);
                }
                else if (!string.IsNullOrEmpty(SelectedOrganizationMemberId))
                {
                    bookingSuccess = await BookOrganizationAppointment(conn, userId);
                }
                else
                {
                    TempData["Error"] = "No appointment target selected.";
                    return RedirectToPage();
                }

                if (bookingSuccess)
                {
                    TempData["Message"] = "Appointment successfully booked!";
                    return RedirectToPage("/makeAppointment");
                }
                else
                {
                    // Error messages are already set in the individual booking methods
                    return RedirectToPage();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error booking appointment: " + ex.Message);
                TempData["Error"] = "There was an error booking the appointment. Please try again.";
                return RedirectToPage();
            }
        }
        private async Task<bool> BookProfessorAppointment(SqlConnection conn, string userId)
        {
            // First check availability and capacity
            bool isAvailable = await IsProfessorAvailable(conn, SelectedProfessorId,
                AppointmentDate ?? DateTime.Now, AppointmentTime);

            if (!isAvailable)
            {
                TempData["Error"] = "The selected time slot is no longer available or has reached capacity.";
                _logger.LogWarning("Booking failed - professor not available or at capacity");
                return false;
            }

            string query = @"
INSERT INTO dbo.Appointments 
    (student_id, alumni_id, professor_id, appointment_date, 
    appointment_time, purpose, status, additionalnotes, Mode, Year_Section)
VALUES 
    (@StudentID, @AlumniID, @ProfessorId, @AppointmentDate, 
    @AppointmentTime, @Purpose, 'Pending', @AdditionalNotes, @Mode, @YearSection)";

            using SqlCommand cmd = new(query, conn);

            if (UserBaseRole == "Student")
            {
                cmd.Parameters.AddWithValue("@StudentID", userId);
                cmd.Parameters.AddWithValue("@AlumniID", DBNull.Value);
            }
            else if (UserBaseRole == "Alumni")
            {
                cmd.Parameters.AddWithValue("@StudentID", DBNull.Value);
                cmd.Parameters.AddWithValue("@AlumniID", userId);
            }

            cmd.Parameters.AddWithValue("@ProfessorId", SelectedProfessorId);
            cmd.Parameters.AddWithValue("@AppointmentDate", AppointmentDate ?? DateTime.Now);
            cmd.Parameters.AddWithValue("@AppointmentTime", AppointmentTime);
            cmd.Parameters.AddWithValue("@Purpose", Purpose);
            cmd.Parameters.AddWithValue("@AdditionalNotes", AdditionalNotes ?? "");
            cmd.Parameters.AddWithValue("@Mode", Mode);
            cmd.Parameters.AddWithValue("@YearSection", YearSection);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Professor appointment booked successfully.");
            return true;
        }

        private async Task<bool> BookAlumniAppointment(SqlConnection conn, string userId)
        {
            if (UserBaseRole != "Student")
            {
                TempData["Error"] = "Only students can book appointments with alumni.";
                _logger.LogWarning("Non-student attempted to book alumni appointment");
                return false;
            }

            // First check availability and capacity
            bool isAvailable = await IsAlumniAvailable(conn, SelectedAlumniId,
                AppointmentDate ?? DateTime.Now, AppointmentTime);

            if (!isAvailable)
            {
                TempData["Error"] = "The selected time slot is no longer available or has reached capacity.";
                _logger.LogWarning("Booking failed - alumni not available or at capacity");
                return false;
            }

            string query = @"
INSERT INTO dbo.StudentAlumniAppointments 
    (StudentID, AlumniID, AppointmentDate, AppointmentTime, 
     Status, Mode, AdditionalNotes, Purpose, Year_Section)
VALUES 
    (@StudentID, @AlumniID, @AppointmentDate, @AppointmentTime, 
     'Pending', @Mode, @AdditionalNotes, @Purpose, @YearSection)";

            using SqlCommand cmd = new(query, conn);

            cmd.Parameters.AddWithValue("@StudentID", userId);
            cmd.Parameters.AddWithValue("@AlumniID", SelectedAlumniId);
            cmd.Parameters.AddWithValue("@AppointmentDate", AppointmentDate ?? DateTime.Now);
            cmd.Parameters.AddWithValue("@AppointmentTime", AppointmentTime);
            cmd.Parameters.AddWithValue("@Mode", Mode);
            cmd.Parameters.AddWithValue("@AdditionalNotes", AdditionalNotes ?? "");
            cmd.Parameters.AddWithValue("@Purpose", Purpose);
            cmd.Parameters.AddWithValue("@YearSection", YearSection);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Alumni appointment booked successfully.");
            return true;
        }

        private async Task<bool> BookOrganizationAppointment(SqlConnection conn, string userId)
        {
            try
            {
                string orgMemberQuery = @"
SELECT 
    om.StudentId, 
    om.OrganizationID,
    u.first_name, 
    u.last_name
FROM dbo.OrganizationMembers om
JOIN dbo.userStudents u ON om.StudentId = u.student_id
WHERE om.StudentId = @StudentId";

                OrganizationMember orgMember = null;

                using (SqlCommand memberCmd = new(orgMemberQuery, conn))
                {
                    memberCmd.Parameters.AddWithValue("@StudentId", SelectedOrganizationMemberId);
                    using (var reader = await memberCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            orgMember = new OrganizationMember
                            {
                                StudentId = reader["StudentId"]?.ToString() ?? string.Empty,
                                FirstName = reader["first_name"]?.ToString() ?? string.Empty,
                                LastName = reader["last_name"]?.ToString() ?? string.Empty,
                                OrganizationId = reader["OrganizationID"] as int?
                            };
                        }
                    }
                }

                if (orgMember == null)
                {
                    TempData["Error"] = "Organization member not found in database.";
                    _logger.LogError("Organization member not found");
                    return false;
                }

                // First check availability and capacity
                bool isAvailable = await IsOrganizationMemberAvailable(
                    conn,
                    orgMember.StudentId,
                    orgMember.OrganizationId,
                    AppointmentDate ?? DateTime.Now,
                    AppointmentTime
                );

                if (!isAvailable)
                {
                    TempData["Error"] = "The selected time slot is no longer available or has reached capacity.";
                    _logger.LogWarning("Booking failed - organization member not available or at capacity");
                    return false;
                }

                string query;
                SqlCommand cmd;

                if (UserBaseRole == "Student")
                {
                    query = @"
INSERT INTO dbo.StudentOrganizationAppointments 
    (StudentID, OrganizationMemberID, AppointmentDate, 
     AppointmentTime, Status, Mode, AdditionalNotes, Purpose, Year_Section)
VALUES 
    (@StudentID, @OrganizationMemberID, @AppointmentDate, 
     @AppointmentTime, 'Pending', @Mode, @AdditionalNotes, @Purpose, @YearSection)";

                    cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@StudentID", userId);
                }
                else if (UserBaseRole == "Alumni")
                {
                    query = @"
INSERT INTO dbo.StudentOrganizationAppointments 
    (AlumniID, OrganizationMemberID, AppointmentDate, 
     AppointmentTime, Status, Mode, AdditionalNotes, Purpose, Year_Section)
VALUES 
    (@AlumniID, @OrganizationMemberID, @AppointmentDate, 
     @AppointmentTime, 'Pending', @Mode, @AdditionalNotes, @Purpose, @YearSection)";

                    cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@AlumniID", userId);
                }
                else
                {
                    TempData["Error"] = "Invalid user role for organization booking";
                    _logger.LogError("Invalid user role for organization booking");
                    return false;
                }

                cmd.Parameters.AddWithValue("@OrganizationMemberID", orgMember.StudentId);
                cmd.Parameters.AddWithValue("@AppointmentDate", AppointmentDate ?? DateTime.Now);
                cmd.Parameters.AddWithValue("@AppointmentTime", AppointmentTime);
                cmd.Parameters.AddWithValue("@Mode", Mode);
                cmd.Parameters.AddWithValue("@AdditionalNotes", AdditionalNotes ?? "");
                cmd.Parameters.AddWithValue("@Purpose", Purpose);
                cmd.Parameters.AddWithValue("@YearSection", YearSection);

                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Organization appointment booked successfully.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error booking organization appointment: {ex.Message}");
                TempData["Error"] = "There was an error booking the appointment. Please try again.";
                return false;
            }
        }

        private async Task<bool> IsProfessorAvailable(SqlConnection conn, int professorId, DateTime appointmentDate, TimeSpan appointmentTime)
        {
            string availabilityQuery = @"
SELECT 
    COUNT(*) AS AvailableSlots,
    (
        SELECT COUNT(*) 
        FROM dbo.Appointments a
        WHERE a.professor_id = @ProfessorId
        AND a.appointment_date = @AppointmentDate
        AND a.appointment_time = @AppointmentTime
        AND a.status NOT IN ('Cancelled', 'Rejected')
    ) AS BookedSlots,
    (
        SELECT TOP 1 ISNULL(pa.capacity, 1)
        FROM dbo.professor_availability pa
        WHERE pa.professor_id = @ProfessorId
        AND pa.status = 'Available'
        AND (
            (pa.available_date IS NULL OR pa.available_date = @AppointmentDate)
        )
        AND (
            (pa.start_time IS NULL AND pa.end_time IS NULL) OR
            (@AppointmentTime BETWEEN pa.start_time AND pa.end_time)
        )
        ORDER BY pa.available_date DESC
    ) AS SlotCapacity
FROM dbo.professor_availability pa
WHERE pa.professor_id = @ProfessorId
AND pa.status = 'Available'
AND (
    (pa.available_date IS NULL OR pa.available_date = @AppointmentDate)
)
AND (
    (pa.start_time IS NULL AND pa.end_time IS NULL) OR
    (@AppointmentTime BETWEEN pa.start_time AND pa.end_time)
)";

            using SqlCommand cmd = new(availabilityQuery, conn);
            cmd.Parameters.AddWithValue("@ProfessorId", professorId);
            cmd.Parameters.AddWithValue("@AppointmentDate", appointmentDate.Date);
            cmd.Parameters.AddWithValue("@AppointmentTime", appointmentTime);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int availableSlots = reader.GetInt32(0);
                int bookedSlots = reader.GetInt32(1);
                int slotCapacity = reader.GetInt32(2);

                // If capacity is null in DB, it will be returned as 1 (default)
                bool isAvailable = availableSlots > 0 && bookedSlots < slotCapacity;

                if (!isAvailable)
                {
                    _logger.LogWarning($"Professor {professorId} at {appointmentDate} {appointmentTime} - " +
                                     $"Available: {availableSlots}, Booked: {bookedSlots}, Capacity: {slotCapacity}");
                }

                return isAvailable;
            }

            return false;
        }

        private async Task<bool> IsAlumniAvailable(SqlConnection conn, string alumniId, DateTime appointmentDate, TimeSpan appointmentTime)
        {
            string availabilityQuery = @"
SELECT 
    COUNT(*) AS AvailableSlots,
    (
        SELECT COUNT(*) 
        FROM dbo.StudentAlumniAppointments a
        WHERE a.AlumniID = @AlumniId
        AND a.AppointmentDate = @AppointmentDate
        AND a.AppointmentTime = @AppointmentTime
        AND a.Status NOT IN ('Cancelled', 'Declined', 'Completed')
    ) AS BookedSlots,
    ISNULL((
        SELECT TOP 1 aa.capacity
        FROM dbo.AlumniAvailability aa
        WHERE aa.alumni_id = @AlumniId
        AND aa.is_available = 1
        AND (aa.available_date IS NULL OR aa.available_date = @AppointmentDate)
        AND (
            (aa.start_time IS NULL AND aa.end_time IS NULL) OR
            (@AppointmentTime >= aa.start_time AND @AppointmentTime < aa.end_time)
        )
        ORDER BY aa.available_date DESC
    ), 1) AS SlotCapacity
FROM dbo.AlumniAvailability aa
WHERE aa.alumni_id = @AlumniId
AND aa.is_available = 1
AND (aa.available_date IS NULL OR aa.available_date = @AppointmentDate)
AND (
    (aa.start_time IS NULL AND aa.end_time IS NULL) OR
    (@AppointmentTime >= aa.start_time AND @AppointmentTime < aa.end_time)
)";

            using SqlCommand cmd = new(availabilityQuery, conn);
            cmd.Parameters.AddWithValue("@AlumniId", alumniId);
            cmd.Parameters.AddWithValue("@AppointmentDate", appointmentDate.Date);
            cmd.Parameters.AddWithValue("@AppointmentTime", appointmentTime);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int availableSlots = reader.GetInt32(0);
                int bookedSlots = reader.GetInt32(1);
                int slotCapacity = reader.GetInt32(2);

                bool isAvailable = availableSlots > 0 && bookedSlots < slotCapacity;

                _logger.LogInformation($"Alumni {alumniId} at {appointmentDate} {appointmentTime} - " +
                                     $"Available: {availableSlots}, Booked: {bookedSlots}, Capacity: {slotCapacity}");

                return isAvailable;
            }
            return false;
        }
        private async Task<bool> IsOrganizationMemberAvailable(SqlConnection conn, string studentId, int? organizationId, DateTime appointmentDate, TimeSpan appointmentTime)
        {
            string availabilityQuery = @"
SELECT 
    COUNT(*) AS AvailableSlots,
    (
        SELECT COUNT(*) 
        FROM dbo.StudentOrganizationAppointments a
        WHERE a.OrganizationMemberID = @StudentId
        AND a.AppointmentDate = @AppointmentDate
        AND a.AppointmentTime = @AppointmentTime
        AND a.Status NOT IN ('Cancelled', 'Declined', 'Completed')
    ) AS BookedSlots,
    ISNULL((
        SELECT TOP 1 oa.MaxCapacity
        FROM dbo.OrganizationAvailability oa
        WHERE oa.StudentId = @StudentId
        AND oa.OrganizationID = @OrganizationId
        AND oa.Status = 'available'
        AND (oa.AvailableDate IS NULL OR oa.AvailableDate = @AppointmentDate)
        AND (
            (oa.StartTime IS NULL AND oa.EndTime IS NULL) OR
            (@AppointmentTime >= oa.StartTime AND @AppointmentTime < oa.EndTime)
        )
        ORDER BY oa.AvailableDate DESC
    ), 1) AS SlotCapacity
FROM dbo.OrganizationAvailability oa
WHERE oa.StudentId = @StudentId
AND oa.OrganizationID = @OrganizationId
AND oa.Status = 'available'
AND (oa.AvailableDate IS NULL OR oa.AvailableDate = @AppointmentDate)
AND (
    (oa.StartTime IS NULL AND oa.EndTime IS NULL) OR
    (@AppointmentTime >= oa.StartTime AND @AppointmentTime < oa.EndTime)
)";

            using SqlCommand cmd = new(availabilityQuery, conn);
            cmd.Parameters.AddWithValue("@StudentId", studentId);
            cmd.Parameters.AddWithValue("@OrganizationId", organizationId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@AppointmentDate", appointmentDate.Date);
            cmd.Parameters.AddWithValue("@AppointmentTime", appointmentTime);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int availableSlots = reader.GetInt32(0);
                int bookedSlots = reader.GetInt32(1);
                int slotCapacity = reader.GetInt32(2);

                bool isAvailable = availableSlots > 0 && bookedSlots < slotCapacity;

                _logger.LogInformation($"Org member {studentId} at {appointmentDate} {appointmentTime} - " +
                                     $"Available: {availableSlots}, Booked: {bookedSlots}, Capacity: {slotCapacity}");

                return isAvailable;
            }
            return false;
        }

    }
}
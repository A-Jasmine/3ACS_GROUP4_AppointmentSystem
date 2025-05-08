using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System;

namespace WebBookingApp.Pages
{
    public class editEmployeeModel : PageModel
    {
        private readonly ILogger<editEmployeeModel> _logger;
        private readonly string _connString;

        [BindProperty]
        public Employee SelectedEmployee { get; set; }
        public string Message { get; private set; } = "";

        // Constructor: Inject ILogger and IConfiguration
        public editEmployeeModel(ILogger<editEmployeeModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            // Retrieve connection string from configuration
            _connString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(_connString))
            {
                _logger.LogError("Connection string 'DefaultConnection' is missing from the configuration.");
                throw new InvalidOperationException("Connection string not found in the configuration.");
            }
        }

        public void OnGet(int professorId)
        {
            _logger.LogInformation($"OnGet() started for professorId: {professorId}");


            // Initialize SelectedEmployee first
            SelectedEmployee = new Employee { ProfessorId = professorId };

            using SqlConnection conn = new(_connString);
            conn.Open();

            string query = @"
            SELECT 
                first_name, middle_name, last_name, contact_number, gender, 
                date_of_birth, email, address, city, program, role, employment_status, joining_date
            FROM dbo.userCICT
            WHERE professor_id = @ProfessorId";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@ProfessorId", professorId);

                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    SelectedEmployee.FirstName = reader["first_name"].ToString();
                    SelectedEmployee.LastName = reader["last_name"].ToString();
                    SelectedEmployee.MiddleName = reader["middle_name"].ToString();
                    SelectedEmployee.ContactNumber = reader["contact_number"].ToString();
                    SelectedEmployee.Gender = reader["gender"].ToString();
                    SelectedEmployee.DateOfBirth = reader.GetDateTime(reader.GetOrdinal("date_of_birth"));
                    SelectedEmployee.EmailAddress = reader["email"].ToString();
                    SelectedEmployee.Address = reader["address"].ToString();
                    SelectedEmployee.City = reader["city"].ToString();
                    SelectedEmployee.Program = reader["program"].ToString();
                    SelectedEmployee.Role = reader["role"].ToString();
                    SelectedEmployee.EmploymentStatus = reader["employment_status"].ToString();
                    SelectedEmployee.JoiningDate = reader.GetDateTime(reader.GetOrdinal("joining_date"));
                    _logger.LogInformation($"Employee data for ProfessorId {professorId} retrieved.");
                }
                else
                {
                    _logger.LogWarning($"No employee found with professor_id {professorId}");
                }
            }

            // Now load the student organization assignment
            LoadStudentOrganizationAssignment(professorId);
        }
        private void LoadStudentOrganizationAssignment(int professorId)
        {
            using SqlConnection conn = new(_connString);
            conn.Open();

            string query = @"
    SELECT so.OrganizationName, ia.Role
    FROM dbo.InstructorAdvisers ia
    JOIN dbo.StudentOrganizations so ON ia.OrganizationID = so.OrganizationID
    WHERE ia.InstructorID = @ProfessorId";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@ProfessorId", professorId);

                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    // Normalize the case to match your dropdown options
                    var orgName = reader["OrganizationName"].ToString();
                    var role = reader["Role"].ToString();

                    // Convert to uppercase to match your dropdown options
                    SelectedEmployee.StudentOrg = $"{orgName.ToUpper()} - {role}";
                    _logger.LogInformation($"Loaded student org assignment: {SelectedEmployee.StudentOrg}");
                }
                else
                {
                    SelectedEmployee.StudentOrg = "None";
                    _logger.LogInformation("No student org assignment found, setting to None");
                }
            }
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            _logger.LogInformation("OnPostSaveAsync method triggered.");

            if (!ModelState.IsValid)
            {
                Message = "Please ensure all required fields are filled out.";
                _logger.LogError("Validation failed.");
                return Page();
            }

            using (SqlConnection conn = new(_connString))
            {
                await conn.OpenAsync();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // Step 1: Update Employee Data
                        string updateEmployeeQuery = @"
                        UPDATE dbo.userCICT 
                        SET first_name = @FirstName,
                            middle_name = @MiddleName,
                            last_name = @LastName,
                            contact_number = @ContactNumber,
                            gender = @Gender,
                            date_of_birth = @DateOfBirth,
                            email = @EmailAddress,
                            address = @Address,
                            city = @City,
                            program = @Program,
                            role = @Role,
                            employment_status = @EmploymentStatus,
                            joining_date = @JoiningDate
                        WHERE professor_id = @ProfessorId";

                        using (SqlCommand cmd = new(updateEmployeeQuery, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@FirstName", SelectedEmployee.FirstName);
                            cmd.Parameters.AddWithValue("@MiddleName", string.IsNullOrEmpty(SelectedEmployee.MiddleName) ? (object)DBNull.Value : SelectedEmployee.MiddleName);
                            cmd.Parameters.AddWithValue("@LastName", SelectedEmployee.LastName);
                            cmd.Parameters.AddWithValue("@ContactNumber", string.IsNullOrEmpty(SelectedEmployee.ContactNumber) ? (object)DBNull.Value : SelectedEmployee.ContactNumber);
                            cmd.Parameters.AddWithValue("@Gender", string.IsNullOrEmpty(SelectedEmployee.Gender) ? (object)DBNull.Value : SelectedEmployee.Gender);
                            cmd.Parameters.AddWithValue("@DateOfBirth", SelectedEmployee.DateOfBirth ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@EmailAddress", SelectedEmployee.EmailAddress);
                            cmd.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(SelectedEmployee.Address) ? (object)DBNull.Value : SelectedEmployee.Address);
                            cmd.Parameters.AddWithValue("@City", string.IsNullOrEmpty(SelectedEmployee.City) ? (object)DBNull.Value : SelectedEmployee.City);
                            cmd.Parameters.AddWithValue("@Program", SelectedEmployee.Program);
                            cmd.Parameters.AddWithValue("@Role", SelectedEmployee.Role);
                            cmd.Parameters.AddWithValue("@EmploymentStatus", string.IsNullOrEmpty(SelectedEmployee.EmploymentStatus) ? (object)DBNull.Value : SelectedEmployee.EmploymentStatus);
                            cmd.Parameters.AddWithValue("@JoiningDate", SelectedEmployee.JoiningDate ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ProfessorId", SelectedEmployee.ProfessorId);

                            int rowsAffected = await cmd.ExecuteNonQueryAsync();

                            if (rowsAffected > 0)
                            {
                                _logger.LogInformation($"Employee profile with ProfessorId {SelectedEmployee.ProfessorId} updated successfully.");
                                Message = "Employee profile updated successfully!";
                            }
                            else
                            {
                                _logger.LogWarning($"No rows were affected for ProfessorId {SelectedEmployee.ProfessorId}. It might not exist.");
                                Message = "No changes were made. Please check the professor ID.";
                                transaction.Rollback();
                                return Page();
                            }
                        }

                        // Step 2: Handle Adviser Relationship
                        await HandleAdviserRelationship(conn, transaction);

                        transaction.Commit();
                        return RedirectToPage("/viewEmployee");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Message = "Error updating employee profile: " + ex.Message;
                        _logger.LogError("Error updating profile: " + ex.Message);
                        return Page();
                    }
                }
            }
        }

        private async Task HandleAdviserRelationship(SqlConnection conn, SqlTransaction transaction)
        {
            // First, remove any existing adviser relationships
            string deleteQuery = "DELETE FROM dbo.InstructorAdvisers WHERE InstructorID = @InstructorID";
            using (SqlCommand cmd = new(deleteQuery, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@InstructorID", SelectedEmployee.ProfessorId);
                await cmd.ExecuteNonQueryAsync();
            }

            // If a new organization is selected, add the relationship
            if (!string.IsNullOrEmpty(SelectedEmployee.StudentOrg) && !SelectedEmployee.StudentOrg.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                var orgRoleParts = SelectedEmployee.StudentOrg.Split(" - ");
                if (orgRoleParts.Length != 2)
                {
                    throw new Exception("Invalid format for Student Organization. Ensure it includes both Organization and Role.");
                }

                var organizationName = orgRoleParts[0];
                var role = orgRoleParts[1];

                // Get OrganizationID - use case-insensitive comparison
                string getOrganizationIdQuery = @"
            SELECT OrganizationID 
            FROM dbo.StudentOrganizations 
            WHERE UPPER(OrganizationName) = UPPER(@OrganizationName)";

                int organizationId = 0;

                using (SqlCommand cmd = new(getOrganizationIdQuery, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@OrganizationName", organizationName);
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read())
                    {
                        organizationId = reader.GetInt32(reader.GetOrdinal("OrganizationID"));
                    }
                    else
                    {
                        throw new Exception($"Organization {organizationName} not found in the database.");
                    }
                }

                // Insert new adviser relationship
                string insertAdviserQuery = @"
            INSERT INTO dbo.InstructorAdvisers 
            (InstructorID, OrganizationID, Role, AssignedAt)
            VALUES (@InstructorID, @OrganizationID, @Role, @AssignedAt)";

                using (SqlCommand cmd = new(insertAdviserQuery, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@InstructorID", SelectedEmployee.ProfessorId);
                    cmd.Parameters.AddWithValue("@OrganizationID", organizationId);
                    cmd.Parameters.AddWithValue("@Role", role);
                    cmd.Parameters.AddWithValue("@AssignedAt", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Instructor with ProfessorId {SelectedEmployee.ProfessorId} assigned to organization {organizationName} as {role}.");
                }
            }
        }

        public class Employee
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string MiddleName { get; set; }
            public string ContactNumber { get; set; }
            public string Gender { get; set; }
            public DateTime? DateOfBirth { get; set; }
            public string EmailAddress { get; set; }
            public string Address { get; set; }
            public string City { get; set; }
            public string Program { get; set; }
            public string Role { get; set; }
            public string EmploymentStatus { get; set; }
            public DateTime? JoiningDate { get; set; }
            public int ProfessorId { get; set; }
            public string StudentOrg { get; set; }
        }
    }
}
using Microsoft.AspNetCore.Authentication.Cookies;
using WebBookingApp.Pages;
using WebBookingApp.Services;
using static WebBookingApp.Pages.setAvailabilitymodel;

var builder = WebApplication.CreateBuilder(args);




// Add services to the container.
builder.Services.AddRazorPages();

// Add this line where you configure services
builder.Services.AddHostedService<AvailabilityCleanupService>();

// ✅ Add Authentication Service
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login"; // Redirects to login if not authenticated
        options.AccessDeniedPath = "/AccessDenied"; // (Optional) Redirects to access denied page
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60); // Session expiration time
    });

builder.Services.AddAuthorization();

var app = builder.Build();



app.UseDeveloperExceptionPage();


app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // ✅ Ensure it's before Authorization
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/", context =>
{
    context.Response.Redirect("/Login");
    return Task.CompletedTask;
});

app.Run();


using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text.Json;
using System.IO;


var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Global exception handler (first)
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var error = new { error = "An unexpected error occurred. Please try again later." };
        await context.Response.WriteAsJsonAsync(error);
    });
});

// Authentication middleware (second)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/users"))
    {
        var token = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token) || !IsValidToken(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized: Invalid or missing token." });
            return;
        }
    }
    await next();
});

// Logging middleware (last)
app.Use(async (context, next) =>
{
    Console.WriteLine($"Request: {context.Request.Method} {context.Request.Path}");
    if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;
        if (!string.IsNullOrWhiteSpace(body))
            Console.WriteLine($"Request Body: {body}");
    }

    var originalBodyStream = context.Response.Body;
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;

    await next();

    context.Response.Body.Seek(0, SeekOrigin.Begin);
    var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
    context.Response.Body.Seek(0, SeekOrigin.Begin);
    Console.WriteLine($"Response: {context.Response.StatusCode}");
    if (!string.IsNullOrWhiteSpace(responseText))
        Console.WriteLine($"Response Body: {responseText}");

    await responseBody.CopyToAsync(originalBodyStream);
    context.Response.Body = originalBodyStream;
});

// Simple token validation (replace with real logic as needed)
static bool IsValidToken(string token)
{
    return token == "Bearer mysecrettoken";
}

// In-memory user store
var users = new ConcurrentDictionary<int, User>();
var nextId = 1;

// Create user
app.MapPost("/users", async (HttpContext context) => {
    var user = await JsonSerializer.DeserializeAsync<User>(context.Request.Body);
    if (user is null || string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Email) || !IsValidEmail(user.Email))
        return Results.BadRequest("Invalid user data or email.");
    var id = nextId++;
    var newUser = user with { Id = id };
    users[id] = newUser;
    return Results.Created($"/users/{id}", newUser);
});

// Get all users
app.MapGet("/users", () => users.Values);

// Get user by id
app.MapGet("/users/{id:int}", (int id) =>
    users.TryGetValue(id, out var user)
        ? Results.Ok(user)
        : Results.NotFound(new { error = $"User with id {id} not found." })
);

// Update user
app.MapPut("/users/{id:int}", async (int id, HttpContext context) => {
    if (!users.ContainsKey(id)) return Results.NotFound();
    var updated = await JsonSerializer.DeserializeAsync<User>(context.Request.Body);
    if (updated is null || string.IsNullOrWhiteSpace(updated.Name) || string.IsNullOrWhiteSpace(updated.Email) || !IsValidEmail(updated.Email))
        return Results.BadRequest("Invalid user data or email.");
    var newUser = updated with { Id = id };
    users[id] = newUser;
    return Results.Ok(newUser);
});

// Delete user
app.MapDelete("/users/{id:int}", (int id) =>
    users.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound()
);

app.MapGet("/users/throw", (HttpContext context) => throw new Exception("Test exception"));

app.Run();

// Basic email validation
static bool IsValidEmail(string email)
{
    if (string.IsNullOrWhiteSpace(email)) return false;
    try
    {
        var addr = new System.Net.Mail.MailAddress(email);
        return addr.Address == email;
    }
    catch
    {
        return false;
    }
}

// User model
record User(int Id, string Name, string Email);

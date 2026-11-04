using Microsoft.EntityFrameworkCore;
using sem1.Data;
using Microsoft.Extensions.ML;
using sem1.Models;
using sem1.Api;
using Scalar.AspNetCore;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("LibraryContext") ?? throw new InvalidOperationException("Connection string 'LibraryContext' not found.");

builder.Services.AddDbContext<LibraryContext>(options => options.UseSqlServer(connectionString));

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services
    .AddPredictionEnginePool<BookRating, BookRatingPrediction>()
    .FromFile(
        modelName: "BookRecommender",
        filePath: "MLModels/BookRecommender.zip",
        watchForChanges: true);
builder.Services.AddValidation();
builder.Services.AddOpenApi();

var token = builder.Configuration["Gemini:ApiKey"]
    ?? throw new InvalidOperationException(
        "Lipseste Gemini:ApiKey din User Secrets.");

var endpoint = new Uri(
    "https://generativelanguage.googleapis.com/v1beta/openai/");

IChatClient chat = new OpenAIClient(
    new ApiKeyCredential(token),
    new OpenAIClientOptions { Endpoint = endpoint })
    .GetChatClient("gemini-3.5-flash-lite")
    .AsIChatClient();

builder.Services.AddChatClient(chat);

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryContext>();
    db.Database.Migrate();
    LibrarySeed.Seed(db);
}

app.MapPost("/api/v2/books/{id:int}/pitch",
    async (int id, LibraryContext db, IChatClient ai) =>
    {
        var b = await db.Book
            .Include(x => x.Author)
            .Include(x => x.Genre)
            .FirstOrDefaultAsync(x => x.ID == id);

        if (b is null) return Results.NotFound();

        List<ChatMessage> messages =
        [
            new(ChatRole.System,
            "Ești bibliotecar. Răspunde în română, în cel mult 2 propoziții."),
        new(ChatRole.User,
            $"Recomandă „{b.Title}” de {b.Author?.FullName} " +
            $"({b.Genre?.Name}). Descriere: {b.Description}")
        ];

        ChatResponse r = await ai.GetResponseAsync(messages);

        return Results.Ok(new { b.Title, Pitch = r.Text });
    });

app.MapGet("/api/v2/ask", async (string q, IChatClient ai) =>
    (await ai.GetResponseAsync(q)).Text);

app.MapLibraryApi();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "start",
    pattern: "start",
    defaults: new { controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

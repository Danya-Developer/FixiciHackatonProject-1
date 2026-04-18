// Program.cs (исправленная версия)
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

// Настройка статических файлов
app.UseDefaultFiles();
app.UseStaticFiles();

// Путь к файлам данных
string dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

string teamsFile = Path.Combine(dataPath, "teams.json");
string criteriaFile = Path.Combine(dataPath, "criteria.json");
string scoresFile = Path.Combine(dataPath, "scores.json");
string chatFile = Path.Combine(dataPath, "chat.json");
string timerFile = Path.Combine(dataPath, "timer.json");

// Helper для чтения/записи JSON
T ReadData<T>(string file) where T : new()
{
    if (!File.Exists(file)) return new T();
    try
    {
        string json = File.ReadAllText(file);
        return JsonSerializer.Deserialize<T>(json) ?? new T();
    }
    catch { return new T(); }
}

void WriteData<T>(string file, T data)
{
    string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(file, json);
}

// API endpoints
app.MapGet("/api/teams", () => ReadData<List<Team>>(teamsFile));

app.MapPost("/api/teams", async (HttpContext context) =>
{
    var team = await context.Request.ReadFromJsonAsync<Team>();
    var teams = ReadData<List<Team>>(teamsFile);
    if (team != null)
    {
        team.Id = team.Id ?? Guid.NewGuid().ToString();
        teams.Add(team);
        WriteData(teamsFile, teams);
    }
    return Results.Ok(team);
});

app.MapPut("/api/teams", async (HttpContext context) =>
{
    var team = await context.Request.ReadFromJsonAsync<Team>();
    var teams = ReadData<List<Team>>(teamsFile);
    if (team != null)
    {
        var existing = teams.FirstOrDefault(t => t.Id == team.Id);
        if (existing != null)
        {
            existing.Name = team.Name;
            existing.Members = team.Members;
            existing.Link = team.Link;
            existing.Id = team.Id;
        }
        WriteData(teamsFile, teams);
    }
    return Results.Ok();
});

app.MapDelete("/api/teams/{id}", (string id) =>
{
    var teams = ReadData<List<Team>>(teamsFile);
    teams.RemoveAll(t => t.Id == id);
    WriteData(teamsFile, teams);

    var scores = ReadData<List<ScoreItem>>(scoresFile);
    scores.RemoveAll(s => s.TeamId == id);
    WriteData(scoresFile, scores);

    return Results.Ok();
});

app.MapGet("/api/criteria", () => ReadData<List<Criterion>>(criteriaFile));

app.MapPost("/api/criteria", async (HttpContext context) =>
{
    var criterion = await context.Request.ReadFromJsonAsync<Criterion>();
    var criteria = ReadData<List<Criterion>>(criteriaFile);
    if (criterion != null)
    {
        criterion.Id = criterion.Id ?? Guid.NewGuid().ToString();
        criteria.Add(criterion);
        WriteData(criteriaFile, criteria);
    }
    return Results.Ok(criterion);
});

app.MapDelete("/api/criteria/{id}", (string id) =>
{
    var criteria = ReadData<List<Criterion>>(criteriaFile);
    criteria.RemoveAll(c => c.Id == id);
    WriteData(criteriaFile, criteria);

    var scores = ReadData<List<ScoreItem>>(scoresFile);
    scores.RemoveAll(s => s.CriterionId == id);
    WriteData(scoresFile, scores);

    return Results.Ok();
});

app.MapGet("/api/scores", () => ReadData<List<ScoreItem>>(scoresFile));

app.MapPost("/api/scores", async (HttpContext context) =>
{
    var scores = await context.Request.ReadFromJsonAsync<List<ScoreItem>>();
    if (scores != null)
    {
        WriteData(scoresFile, scores);
    }
    return Results.Json(scores);
});

app.MapGet("/api/chat", () => ReadData<List<ChatMessage>>(chatFile));

app.MapPost("/api/chat", async (HttpContext context) =>
{
    var msg = await context.Request.ReadFromJsonAsync<ChatMessage>();
    var messages = ReadData<List<ChatMessage>>(chatFile);
    if (msg != null)
    {
        msg.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        messages.Add(msg);
        while (messages.Count > 200) messages.RemoveAt(0);
        WriteData(chatFile, messages);
    }
    return Results.Json(msg);
});

app.MapGet("/api/timer", () =>
{
    var timer = ReadData<TimerData>(timerFile);
    return Results.Ok(timer);
});

app.MapPost("/api/timer", async (HttpContext context) =>
{
    var timer = await context.Request.ReadFromJsonAsync<TimerData>();
    if (timer != null)
    {
        WriteData(timerFile, timer);
    }
    return Results.Ok();
});

app.MapGet("/api/leaderboard", () =>
{
    var teams = ReadData<List<Team>>(teamsFile);
    var scores = ReadData<List<ScoreItem>>(scoresFile);

    var results = teams.Select(t =>
    {
        var teamScores = scores.Where(s => s.TeamId == t.Id);
        var juryTotals = new Dictionary<string, int>();
        foreach (var s in teamScores)
        {
            if (!juryTotals.ContainsKey(s.JuryName) || s.Total > juryTotals[s.JuryName])
                juryTotals[s.JuryName] = s.Total;
        }
        return new LeaderboardEntry
        {
            Id = t.Id,
            Name = t.Name,
            Members = t.Members,
            Total = juryTotals.Values.Sum(),
            JuryCount = juryTotals.Count
        };
    }).OrderByDescending(r => r.Total).ToList();

    return Results.Ok(results);
});

app.Run();

// Models
public class Team
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public string Link { get; set; } = "";
}

public class Criterion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Min { get; set; }
    public int Max { get; set; }
}

public class ScoreItem
{
    public string TeamId { get; set; } = "";
    public string JuryName { get; set; } = "";
    public string CriterionId { get; set; } = "";
    public int Score { get; set; }
    public int Total { get; set; }
}

public class ChatMessage
{
    public string SenderName { get; set; } = "";
    public string SenderRole { get; set; } = "";
    public string Text { get; set; } = "";
    public string Time { get; set; } = "";
    public long Timestamp { get; set; }
}

public class TimerData
{
    public string EndTime { get; set; } = "";
}

public class LeaderboardEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public int Total { get; set; }
    public int JuryCount { get; set; }
}
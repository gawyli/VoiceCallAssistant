using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using VoiceCallAssistant;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Repository;
using VoiceCallAssistant.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddServices()
    .AddDatabase(builder.Configuration);

//builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//    .AddMicrosoftIdentityWebApi(builder.Configuration);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseWebSockets();

app.UseHttpsRedirection();

//app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

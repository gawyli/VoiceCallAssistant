using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using VoiceCallAssistant;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();
Log.Information("Bootstrapping app");

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddLogging(builder.Logging, builder.Configuration)
        .AddServices()
        .AddDatabase(builder.Configuration);

    //builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    //    .AddMicrosoftIdentityWebApi(builder.Configuration);

    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    

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
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}



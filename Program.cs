using bwkt_webapp.Services;

var builder = WebApplication.CreateBuilder(args);

// Register application services
builder.Services.AddSingleton<IVideoService, VideoService>();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapRazorPages();

app.Run();
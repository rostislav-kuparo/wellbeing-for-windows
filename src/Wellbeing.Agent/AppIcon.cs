namespace Wellbeing.Agent;

static class AppIcon
{
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Wellbeing.Agent.app.ico")
                           ?? throw new InvalidOperationException("Нет ресурса app.ico");
        return new Icon(stream, size);
    }
}

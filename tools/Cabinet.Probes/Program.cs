using Cabinet.Probes;

return args switch
{
    ["hold-lock", var path] => HoldLock(path),
    ["audio-render", .. var rest] when rest.Length == 9 => AudioRender.Run(rest),
    ["editor-geometry", var prefix, var plugin, var format, var log] => EditorGeometry.Run(prefix, plugin, format, log),
    ["editor-interaction", var prefix, var plugin, var format, var log, .. var rest] when rest.Length >= 1 =>
        EditorInteraction.Run(prefix, plugin, format, log, rest),
    ["editor-play", var prefix, var plugin, var format, var log, .. var rest] when rest.Length >= 4 =>
        EditorPlay.Run(prefix, plugin, format, log, rest),
    _ => Usage(),
};

static int HoldLock(string path)
{
    Libc.HoldReadLock(path);
    Console.WriteLine("held");
    Console.ReadLine();
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine(
        "usage: Cabinet.Probes hold-lock PATH | audio-render ARGS… | editor-geometry|editor-interaction|editor-play CARLA PLUGIN FORMAT LOG …");
    return 2;
}

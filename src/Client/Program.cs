using Neobash.PrintBridge.Lan;

var data = ClientConsole.Options(args).GetValueOrDefault("data");
if (string.IsNullOrWhiteSpace(data))
{
    data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Neobash",
        "PrintBridge",
        "Client");
}

ISecretStore secrets;
try
{
    secrets = CurrentUserSecretStore.Create(data);
}
catch (PlatformNotSupportedException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

return await ClientConsole.RunAsync(args, Console.Out, Console.Error, secrets, data);

using RLBotDotNet;
using System.IO;

namespace RLBotCSharpExample
{
    class Program
    {
        static void Main()
        {
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);

            // Read the port from port.cfg.
            const string file = "port.cfg";
            string text = File.ReadAllLines(file)[0];
            int port = int.Parse(text);

            // BotManager is a generic which takes in Atlas as its T-type.
            BotManager<Atlas> botManager = new BotManager<Atlas>();

            // Start the server on the port given in the port.cfg file.
            botManager.Start(port);
        }
    }
}

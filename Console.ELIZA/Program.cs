//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Lifeprojects.de">
//     Class: Program
//     Copyright © Lifeprojects.de 2026
// </copyright>
// <Template>
// 	Version 3.0.2026.1, 08.1.2026
// </Template>
//
// <author>Gerhard Ahrens - Lifeprojects.de</author>
// <email>developer@lifeprojects.de</email>
// <date>01.02.2026 10:20:50</date>
//
// <summary>
// Konsolen Applikation mit Menü
// </summary>
//-----------------------------------------------------------------------

namespace Console.ELIZA
{
    /* Imports from NET Framework */
    using System;

    public class Program
    {
        private static void Main(string[] args)
        {
            string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            DemoDataPath = Path.Combine(new DirectoryInfo(currentDirectory).Parent.Parent.Parent.FullName, "DemoDatei");
            if (Directory.Exists(DemoDataPath) == false)
            {
                Directory.CreateDirectory(DemoDataPath);
            }


            ConsoleMenu.Add("1", "Chat starten", () => MenuPoint1());
            ConsoleMenu.Add("X", "Beenden", () => ApplicationExit());

            do
            {
                _ = ConsoleMenu.SelectKey(2, 2);
            }
            while (true);
        }

        internal static string DemoDataPath { get; private set; }


        private static void ApplicationExit()
        {
            Environment.Exit(0);
        }

        private static void MenuPoint1()
        {
            Console.Clear();

            string databaseName = Path.Combine(DemoDataPath, "ElizaRule.json");

            var eliza = new ElizaCore(databaseName);

            Console.WriteLine("ELIZA: Hallo. Wie geht es dir?");
            Console.WriteLine("(Tippe 'exit' zum Beenden)\n");

            while (true)
            {
                Console.Write("DU: ");
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var response = eliza.Respond(input);
                Console.WriteLine("ELIZA: " + response);
            }

            Console.WriteLine("\nELIZA: Auf Wiedersehen.");
            ConsoleMenu.Wait();
        }
    }
}

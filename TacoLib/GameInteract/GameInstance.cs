using System.Diagnostics;
using System.Linq;

namespace TacoLib.GameInteract
{
    public class GameInstance
    {
        public static Process GetGameProcess()
        {
            //var entriesAll = Process.GetProcesses().Where(x => x.MainWindowTitle == "Guild Wars 2").FirstOrDefault();
            return Process.GetProcessesByName("Gw2-64").FirstOrDefault();
        }
    }
}

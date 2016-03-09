using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Client
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public enum UpdateState
        {
            Nothing,
            UpdateCompleted,
            UpdateFailed
        }

        public UpdateState ProgramUpdateState = UpdateState.Nothing;

        protected override void OnStartup(StartupEventArgs e)
        {
            switch (e.Args.Length)
            {
                case 1:
                    switch (e.Args[0])
                    {
                        case "--updateCompleted":
                            ProgramUpdateState = UpdateState.UpdateCompleted;
                            break;
                        case "--updateFailed":
                            ProgramUpdateState = UpdateState.UpdateFailed;
                            break;
                    }
                    break;
                case 2:
                    switch (e.Args[0])
                    {
                        case "--updateMove":
                            int updateAttempts = 3;
                            while (updateAttempts-- > 0)
                            {
                                try
                                {
                                    File.Copy(Assembly.GetExecutingAssembly().Location, e.Args[2], true);
                                    Process.Start(e.Args[1], "--updateCompleted");
                                    Shutdown();
                                    return;
                                }
                                catch
                                {
                                    Thread.Sleep(5000);
                                }
                            }
                            Process.Start(e.Args[1], "--updateFailed");
                            Shutdown();
                            break;
                    }
                    break;
            }
        }
    }
}

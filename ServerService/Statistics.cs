﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace ServerService
{
    public sealed class Statistics : INotifyPropertyChanged, IDisposable
    {
        private Timer refresh;
        private int interval;
        private DateTime start;

        public event PropertyChangedEventHandler PropertyChanged;

        public void Dispose()
        {
            refresh.Stop();
            refresh.Dispose();
            GC.SuppressFinalize(this);
        }


        private ObservableCollection<IPAddress> players;
        public ObservableCollection<IPAddress> Players
        {
            get
            {
                return players;
            }
            set
            {
                players = value;
                notifyPropertyChanged();
            }
        }

        private int loggingIndicator;

        private int restartCount = 0;
        /// <summary>
        /// Indicates how many times the server has been restarted because of errors
        /// </summary>
        public int RestartCount
        {
            get
            {
                return restartCount;
            }
            private set
            {
                restartCount = value;
                notifyPropertyChanged();
            }
        }

        /// <summary>
        /// Occurs when the statistics are refreshed
        /// </summary>
        public event StatisticsUpdatedEventHandler StatisticsUpdated;

        private string logFolder = "";
        /// <summary>
        /// Indicates, where the LogFile should be stored
        /// </summary>
        public string LogFolder
        {
            get
            {
                return logFolder;
            }
            set
            {
                logFolder = value;
                notifyPropertyChanged();
            }
        }

        /// <summary>
        /// Handles updates statistics (will be removed - use PropertyChanged)
        /// </summary>
        /// <param name="sender">The class that has been updated</param>
        /// <param name="e">Arguments containing various statistics</param>
        public delegate void StatisticsUpdatedEventHandler(object sender, StatisticsUpdatedEventArgs e);

        /// <summary>
        /// The arguments that go with the StatisticsUpdatedEvent
        /// </summary>
        public class StatisticsUpdatedEventArgs : EventArgs
        {
            public int ActivePlayerCount;
            public TimeSpan Runtime;
            public int TotalUniquePlayers;
            public long CurrentMemoryUsage;
            public long PeakMemoryUsage;
            public int RestartCount;
        }

        /// <summary>
        /// Raises the StatisticsUpdated event
        /// </summary>
        private void OnStatisticsUpdated()
        {
            if(StatisticsUpdated != null)
                StatisticsUpdated(this, new StatisticsUpdatedEventArgs(){
                    ActivePlayerCount = this.ActivePlayerCount,
                    Runtime = this.Runtime,
                    TotalUniquePlayers = this.TotalUniquePlayers,
                    CurrentMemoryUsage = this.CurrentMemoryUsage,
                    PeakMemoryUsage = this.PeakMemoryUsage,
                    RestartCount = this.RestartCount
                });
        }

        private bool enabled = false;
        /// <summary>
        /// Indicates if the Statistics are updated
        /// </summary>
        public bool Enabled
        {
            get
            {
                return enabled;
            }
            private set
            {
                enabled = value;
                notifyPropertyChanged();
                notifyPropertyChanged("ButtonText");
            }
        }

        public string ButtonText
        {
            get
            {
                if (enabled)
                    return "Disable statistics";
                else
                    return "Enable statistics";
            }
        }


        /// <summary>
        /// Contains the amount of unique players, deprecated (Use Players.Count instead)
        /// </summary>
        public int TotalUniquePlayers
        {
            get
            {
                return Players.Count;
            }
        }

        private int activePlayerCount = 0;
        /// <summary>
        /// Contains the current amount of connected players (updated periodically and not on connect)
        /// </summary>
        public int ActivePlayerCount
        {
            get
            {
                return activePlayerCount;
            }
            private set
            {
                activePlayerCount = value;
                notifyPropertyChanged();
            }
        }

        private TimeSpan runtime = new TimeSpan(0);
        /// <summary>
        /// The duration of the current sessions
        /// </summary>
        public TimeSpan Runtime
        {
            get
            {
                return runtime;
            }
            set
            {
                runtime = value;
                notifyPropertyChanged();
            }
        }

        public void UpdateRuntime()
        {
            Runtime = DateTime.Now.Subtract(start);
        }

        private long peakMemoryUsage = 0;
        /// <summary>
        /// Contains the maximum amount of memory used by the server
        /// </summary>
        public long PeakMemoryUsage
        {
            get
            {
                return peakMemoryUsage;
            }
            private set
            {
                peakMemoryUsage = value / 1024 / 1024;
                notifyPropertyChanged();
            }
        }

        private long currentMemoryUsage = 0;
        /// <summary>
        /// Contains the current amount of memory used by the server
        /// </summary>
        public long CurrentMemoryUsage
        {
            get
            {
                return currentMemoryUsage;
            }
            private set
            {
                currentMemoryUsage = value / 1024 / 1024;
                notifyPropertyChanged();
            }
        }

        public void UpdateCurrentMemoryUsage()
        {
            CurrentMemoryUsage = (Helper.Server != null) ? Helper.Server.PrivateMemorySize64 : 0;
        }

        /// <summary>
        /// Initializes the statistics and starts the autorefresh
        /// </summary>
        /// <param name="timeout">Update interval of the statistics (in ms)</param>
        public Statistics(int timeout)
        {
            initialize(timeout, true);
        }

        /// <summary>
        /// Initializes the statistics
        /// </summary>
        /// <param name="timeout">Update interval of the statistics (in ms)</param>
        /// <param name="autostart">If the timer should be started</param>
        public Statistics(int timeout, bool autostart)
        {
            initialize(timeout, autostart);
        }

        private void initialize(int timeout, bool autostart)
        {
            interval = timeout;
            start = DateTime.Now;

            Helper.ServerRestarted += Helper_ServerRestarted;

            #if DEBUG

            start = start.Subtract(new TimeSpan(5, 0, 0, 0));

            #endif

            players = new ObservableCollection<IPAddress>();

            refresh = new Timer(interval);
            refresh.Elapsed += refresh_Elapsed;

            if (autostart)
            {
                refresh.Start();
                Enabled = true;
            }
        }

        void Helper_ServerRestarted(object sender, EventArgs e)
        {
            IncreaseRestartCount();
        }

        /// <summary>
        /// Starts the statistics timer
        /// </summary>
        public void Start()
        {
            refresh.Start();

            if (LogFolder == "")
                Logging.OnLogMessage("To save your statistics, use the button on the left", Logging.MessageType.Info);

            Enabled = true;
        }

        /// <summary>
        /// Stops the statistics timer
        /// </summary>
        public void Stop()
        {
            refresh.Stop();
            Enabled = false;
        }

        /// <summary>
        /// Updates the statistics
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void refresh_Elapsed(object sender, ElapsedEventArgs e)
        {
            UpdateRuntime();

            if (Validator.Instance.IsRunning())
            {
                updatePlayers(true);

                if (Helper.Server != null)
                {
                    Helper.Server.Refresh();

                    if (PeakMemoryUsage <= (Helper.Server.PrivateMemorySize64 / 1024 / 1024))
                        PeakMemoryUsage = Helper.Server.PrivateMemorySize64;

                    UpdateCurrentMemoryUsage();
                }

                if (LogFolder != "")
                {
                    if ((loggingIndicator % 10) == 0)
                    {
                        try
                        {
                            string targetFile = Path.Combine(LogFolder, String.Format("{0}.{1}", start.ToString("yyyy-MM-dd_HH-mm-ss"), "csv"));

                            StreamWriter sw = null;

                            if (!File.Exists(targetFile))
                            {
                                File.Create(targetFile).Close();
                                sw = File.AppendText(targetFile);
                                sw.WriteLine("Timestamp;Runtime;Current players;Total players;Current memory;Max memory;Number of restarts");
                            }

                            if (sw == null)
                                sw = File.AppendText(targetFile);

                            sw.WriteLine("{0};{1};{2};{3};{4};{5};{6}",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                String.Format("{0}:{1:00}:{2:00}", Math.Floor(Runtime.TotalHours), Runtime.Minutes, Runtime.Seconds),
                                ActivePlayerCount,
                                TotalUniquePlayers,
                                CurrentMemoryUsage,
                                PeakMemoryUsage,
                                RestartCount);

                            sw.Close();
                            loggingIndicator = 1;
                        }
                        catch (Exception ex)
                        {
                            Logging.OnLogMessage(String.Format("Could not write to file: {0}", ex.Message), Logging.MessageType.Error);
                            Logging.OnLogMessage("The log is now incomplete.", Logging.MessageType.Warning);
                        }
                    }
                    else
                    {
                        loggingIndicator++;
                    }
                }

                OnStatisticsUpdated();
            }
        }

        /// <summary>
        /// Counts how many players are connected to the server.
        /// </summary>
        /// <returns>The amount of connected players</returns>
        public int UpdateConnectedPlayerCount()
        {
            updatePlayers(false);
            return ActivePlayerCount;
        }

        /// <summary>
        /// Returns the amount of "hits" this session
        /// </summary>
        /// <returns></returns>
        public int UpdateTotalPlayerCount()
        {
            updatePlayers(true);
            return Players.Count;
        }

        private void updatePlayers(bool updateDictionary)
        {
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            TcpConnectionInformation[] connectionInformation = ipGlobalProperties.GetActiveTcpConnections();

            IEnumerator enumerator = connectionInformation.GetEnumerator();

            int count = 0;

            while (enumerator.MoveNext())
            {
                TcpConnectionInformation info = (TcpConnectionInformation)enumerator.Current;

                if (info.LocalEndPoint.Port == 12345 && info.State == TcpState.Established)
                {
                    count++;

                    if(updateDictionary)
                    {
                        if (!players.Contains(info.RemoteEndPoint.Address))
                            players.Add(info.RemoteEndPoint.Address);
                    }
                }
            }

            ActivePlayerCount = count;
        }

        public void IncreaseRestartCount()
        {
            RestartCount++;
        }

        private void notifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

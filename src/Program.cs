namespace FileCleaner
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json.Linq;
    using TICO.GAUDI.Commons;

    class Program
    {
        static IModuleClient MyModuleClient { get; set; } = null;
        static Logger MyLogger { get; } = Logger.GetLogger(typeof(Program));
        static bool IsReady { get; set; } = false;
        static List<TargetInfo> TargetInfos { get; set; }
        static void Main(string[] args)
        {
            try
            {
                Init().Wait();
            }
            catch (Exception e)
            {
                MyLogger.WriteLog(Logger.LogLevel.ERROR, $"Init failed. {e}", true);
                Environment.Exit(1);
            }

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// </summary>
        static async Task Init()
        {
            // 取得済みのModuleClientを解放する
            if (MyModuleClient != null)
            {
                await MyModuleClient.CloseAsync();
                MyModuleClient.Dispose();
                MyModuleClient = null;
            }

            // 環境変数から送信トピックを判定
            TransportTopic defaultSendTopic = TransportTopic.Iothub;
            string sendTopicEnv = Environment.GetEnvironmentVariable("DefaultSendTopic");
            if (Enum.TryParse(sendTopicEnv, true, out TransportTopic sendTopic))
            {
                MyLogger.WriteLog(Logger.LogLevel.INFO, $"Evironment Variable \"DefaultSendTopic\" is {sendTopicEnv}.");
                defaultSendTopic = sendTopic;
            }
            else
            {
                MyLogger.WriteLog(Logger.LogLevel.DEBUG, "Evironment Variable \"DefaultSendTopic\" is not set.");
            }

            // 環境変数から受信トピックを判定
            TransportTopic defaultReceiveTopic = TransportTopic.Iothub;
            string receiveTopicEnv = Environment.GetEnvironmentVariable("DefaultReceiveTopic");
            if (Enum.TryParse(receiveTopicEnv, true, out TransportTopic receiveTopic))
            {
                MyLogger.WriteLog(Logger.LogLevel.INFO, $"Evironment Variable \"DefaultReceiveTopic\" is {receiveTopicEnv}.");
                defaultReceiveTopic = receiveTopic;
            }
            else
            {
                MyLogger.WriteLog(Logger.LogLevel.DEBUG, "Evironment Variable \"DefaultReceiveTopic\" is not set.");
            }

            // MqttModuleClientを作成
            if (Boolean.TryParse(Environment.GetEnvironmentVariable("M2MqttFlag"), out bool m2mqttFlag) && m2mqttFlag)
            {
                string sasTokenEnv = Environment.GetEnvironmentVariable("SasToken");
                MyModuleClient = new MqttModuleClient(sasTokenEnv, defaultSendTopic: defaultSendTopic, defaultReceiveTopic: defaultReceiveTopic);
            }
            // IoTHubModuleClientを作成
            else
            {
                ITransportSettings[] settings = null;
                string protocolEnv = Environment.GetEnvironmentVariable("TransportProtocol");
                if (Enum.TryParse(protocolEnv, true, out TransportProtocol transportProtocol))
                {
                    MyLogger.WriteLog(Logger.LogLevel.INFO, $"Evironment Variable \"TransportProtocol\" is {protocolEnv}.");
                    settings = transportProtocol.GetTransportSettings();
                }
                else
                {
                    MyLogger.WriteLog(Logger.LogLevel.DEBUG, "Evironment Variable \"TransportProtocol\" is not set.");
                }

                MyModuleClient = await IotHubModuleClient.CreateAsync(settings, defaultSendTopic, defaultReceiveTopic).ConfigureAwait(false);
            }

            // edgeHubへの接続
            while (true)
            {
                try
                {
                    await MyModuleClient.OpenAsync().ConfigureAwait(false);
                    break;
                }
                catch (Exception e)
                {
                    MyLogger.WriteLog(Logger.LogLevel.WARN, $"Open a connection to the Edge runtime is failed. {e.Message}");
                    await Task.Delay(1000);
                }
            }

            // Loggerへモジュールクライアントを設定
            Logger.SetModuleClient(MyModuleClient);

            // 環境変数からログレベルを設定
            string logEnv = Environment.GetEnvironmentVariable(Const.ENV_KEY_LOGLEVEL);
            try
            {
                if (logEnv != null) Logger.SetOutputLogLevel(logEnv);
                MyLogger.WriteLog(Logger.LogLevel.INFO, $"Output log level is: {Logger.OutputLogLevel.ToString()}");
            }
            catch (ArgumentException e)
            {
                MyLogger.WriteLog(Logger.LogLevel.WARN, $"Environment LogLevel does not expected string. Exception:{e.Message}");
            }

            IsReady = false;

            // desiredプロパティの取得
            var twin = await MyModuleClient.GetTwinAsync().ConfigureAwait(false);
            var collection = twin.Properties.Desired;
            IsReady = SetMyProperties(collection);
            MyLogger.WriteLog(Logger.LogLevel.INFO, $"Properties set Result [{IsReady}]");

            // プロパティ更新時のコールバックを登録
            await MyModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, null).ConfigureAwait(false);
            MyLogger.WriteLog(Logger.LogLevel.INFO, $"SetDesiredPropertyUpdateCallbackAsync set.");

            if (IsReady)
            {
                // スケジューラ開始
                await Cleaner.SchedulerStartAsync(TargetInfos);
            }
        }

        /// <summary>
        /// プロパティ更新時のコールバック処理
        /// </summary>
        static async Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            try
            {
                MyLogger.WriteLog(Logger.LogLevel.INFO, "OnDesiredPropertiesUpdate Called.");

                // スケジューラ停止
                await Cleaner.SchedulerStopAsync();

                await Init();
            }
            catch(Exception e)
            {
                MyLogger.WriteLog(Logger.LogLevel.ERROR, $"OnDesiredPropertiesUpdate failed. {e}", true);
            }
        }

        /// <summary>
        /// desiredプロパティから自クラスのプロパティをセットする
        /// </summary>
        /// <returns>desiredプロパティに想定しない値があればfalseを返す</returns>
        static bool SetMyProperties(TwinCollection desiredProperties)
        {
            try
            {
                // Desiredプロパティ読込
                TargetInfos = new List<TargetInfo>();
                for (int i = 1; desiredProperties.Contains(Const.DP_KEY_INFO + i.ToString("D")); i++)
                {
                    JObject jobj = desiredProperties[Const.DP_KEY_INFO + i.ToString("D")];
                    TargetInfos.Add(TargetInfo.CreateInstance(jobj, i));
                }

                // デバッグログ
                if ((int)Logger.OutputLogLevel <= (int)Logger.LogLevel.TRACE)
                {
                    MyLogger.WriteLog(Logger.LogLevel.TRACE, $"DesiredProperties Loaded:");
                    foreach (var info in TargetInfos)
                    {
                        MyLogger.WriteLog(Logger.LogLevel.TRACE, $"{Const.DP_KEY_INFO + info.InfoNumber.ToString("D")}:");
                        MyLogger.WriteLog(Logger.LogLevel.TRACE, info.ToString(Const.DESIRED_DEBUG_INDENT));
                    }
                }
            }
            catch (Exception e)
            {
                MyLogger.WriteLog(Logger.LogLevel.ERROR, $"SetMyProperties failed. {e}", true);
                return false;
            }
            return true;
        }
    }
}

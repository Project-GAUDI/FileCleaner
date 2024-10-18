using Quartz;
using Quartz.Impl;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TICO.GAUDI.Commons;

namespace IotedgeV2FileCleaner
{
    public class Cleaner : IJob
    {
        private static ILogger MyLogger { get; } = LoggerFactory.GetLogger(typeof(Cleaner));

        private static StdSchedulerFactory SchedulerFactory { get; } = new StdSchedulerFactory();

        private static Dictionary<int, TargetInfo> TargetInfos { get; } = new Dictionary<int, TargetInfo>();

        internal static bool IsStart { get; set; } = false;


        /// <summary>
        /// スケジューラ開始処理
        /// </summary>
        /// <param name="infos"></param>
        /// <returns></returns>
        internal static async Task SchedulerStartAsync(List<TargetInfo> infos)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: SchedulerStartAsync");

            // プロパティ情報を初期化して再作成
            TargetInfos.Clear();
            foreach (var info in infos)
            {
                TargetInfos.Add(info.InfoNumber, info);
            }

            // スケジューラ開始
            IScheduler scheduler = await SchedulerFactory.GetScheduler();
            await scheduler.Start();
            IsStart = true;
            MyLogger.WriteLog(ILogger.LogLevel.INFO, "Scheduler Start.");

            // JOBを登録
            foreach (var info in TargetInfos.Values)
            {
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, "Add new ScheduleJob Start.");

                var timeZoneInfo = TimeZoneInfo.Utc;
                if (!string.IsNullOrEmpty(info.TimeZone))
                {
                    timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(info.TimeZone);
                }

                string Schedule = $"{info.Second} {info.Minute} {info.Hour} {info.Day} {info.Month} {info.Week}";
                MyLogger.WriteLog(ILogger.LogLevel.INFO, $"Cron Schedule=[{Schedule}]  TimeZone=[{timeZoneInfo.DisplayName}]  JobName=[{info.JobName}]");

                IJobDetail job = JobBuilder.Create<Cleaner>()
                    .WithIdentity("Job" + info.InfoNumber.ToString("D"))
                    .UsingJobData("info_number", info.InfoNumber.ToString())
                    .Build();
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Create new job.  Key=[{job.Key}]");

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("Trigger" + info.InfoNumber.ToString("D"))
                    .StartNow()
                    .WithCronSchedule(Schedule, x => x.WithMisfireHandlingInstructionFireAndProceed().InTimeZone(timeZoneInfo))
                    .ForJob(job.Key)
                    .Build();
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Create new trigger.  Key=[{trigger.Key}]");

                await scheduler.ScheduleJob(job, trigger);
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, "Add new ScheduleJob End.");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: SchedulerStartAsync");
        }

        /// <summary>
        /// スケジューラ停止処理
        /// </summary>
        /// <returns></returns>
        internal static async Task SchedulerStopAsync()
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: SchedulerStopAsync");

            IsStart = false;
            // 処理中ジョブがある場合は終了まで待機(最大1分)
            foreach (var info in TargetInfos.Values)
            {
                for (int i = 0; i < 60; i++)
                {
                    lock (info.LockObject)
                    {
                        if (!info.IsExecuting)
                        {
                            MyLogger.WriteLog(ILogger.LogLevel.DEBUG, $"'info{info.InfoNumber}' job stopped.");
                            break;
                        }
                    }
                    await Task.Delay(1000);
                }
            }
            // スケジューラ停止
            IScheduler scheduler = await Cleaner.SchedulerFactory.GetScheduler();
            await scheduler.Shutdown();
            MyLogger.WriteLog(ILogger.LogLevel.INFO, "Scheduler Shutdown.");

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: SchedulerStopAsync");
        }

        /// <summary>
        /// スケジューラからJOB起動時刻に実行されるメソッド
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task Execute(IJobExecutionContext context)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: Execute");

            IApplicationEngine appEngine = ApplicationEngineFactory.GetEngine();
            if (ApplicationStateChangeResult.Success == await appEngine.SetApplicationRunningAsync())
            {
                try
                {
                    await Task.Run(() =>
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.INFO, "Execute Start.");
                        JobDataMap datamap = context.JobDetail.JobDataMap;
                        int info_number = int.Parse(datamap.GetString("info_number"));
                        ExecuteFileCleaner(info_number);
                        MyLogger.WriteLog(ILogger.LogLevel.INFO, "Execute End.");
                    });

                }
                catch (Exception ex)
                {
                    var errmsg = $"Execute failed.";
                    MyLogger.WriteLog(ILogger.LogLevel.ERROR, $"{errmsg} {ex}", true);
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: Execute caused by {errmsg}");
                    return;
                }
                finally
                {
                    if (ApplicationStateChangeResult.Success != await appEngine.UnsetApplicationRunningAsync())
                    {
                        var errmsg = $"UnsetApplicationRunningAsync is not 'Success'.";
                        MyLogger.WriteLog(ILogger.LogLevel.ERROR, $"{errmsg}", true);
                    }
                }
            }
            else
            {
                var errmsg = $"SetApplicationRunningAsync is not 'Success'.";
                MyLogger.WriteLog(ILogger.LogLevel.ERROR, $"{errmsg}", true);
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: Execute caused by {errmsg}");
                return;
            }
            
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: Execute");
        }

        /// <summary>
        /// メイン処理（スケジュールされた日時に実行される）
        /// </summary>
        /// <param name="info_number"></param>
        private static void ExecuteFileCleaner(int info_number)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            // スケジューラ停止もしくは終了処理中の場合は処理を抜ける
            bool isCleanerRunning = IsRunning();
            if (!isCleanerRunning)
            {
                var exitmsg = $"isCleanerRunning = {isCleanerRunning}.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {exitmsg}");
                return;
            }

            // プロパティ情報取得
            var info = TargetInfos[info_number];
            if (info == null)
            {
                var errmsg = $"TargetInfo is not found.  info_number={info_number}";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {errmsg}");
                throw new Exception(errmsg);
            }

            // タイムゾーン
            TimeZoneInfo timezone = TimeZoneInfo.Utc;
            if (!string.IsNullOrEmpty(info.TimeZone))
            {
                timezone = TimeZoneInfo.FindSystemTimeZoneById(info.TimeZone);
            }

            // 同一Jobが実行中かチェック(実行中の場合は処理を抜ける)
            lock (info.LockObject)
            {
                if (info.IsExecuting)
                {
                    var dbgmsg = $"FileCleaner Job 'info{info.InfoNumber}' is already executing. skip this run.";
                    MyLogger.WriteLog(ILogger.LogLevel.DEBUG, $"{dbgmsg}");
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {dbgmsg}");
                    return;
                }
                else
                {
                    info.IsExecuting = true;
                }
            }

            try
            {
                MyLogger.WriteLog(ILogger.LogLevel.INFO, $"FileCleaner Job 'info{info.InfoNumber}' Start.  JobName='{info.JobName}'");

                // 処理クラス初期化
                var compObj = new CompressHelper(info);
                var delObj = new DeleteHelper(info);
                var moveObj = new MoveHelper(info);

                // 処理対象とする日時を生成
                DateTime nowDt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
                DateTime? targetDt = CreateTargetDt(info, nowDt);

                // 入力ディレクトリ内のファイルorディレクトリを取得
                FileSystemInfo[] inputFileSystems = GetInputDirAllFiles(info);
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"input dir search end. search result is '{inputFileSystems.Length}'");

                // 処理対象かチェック
                foreach (var filesysinfo in inputFileSystems)
                {
                    // スケジューラ停止の場合は処理を抜ける
                    isCleanerRunning = IsRunning(); 
                    if (!isCleanerRunning)
                    {
                        var exitmsg = $"isCleanerRunning = {isCleanerRunning}.";
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {exitmsg}");
                        return;
                    }

                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"target check start.  Name='{filesysinfo.FullName}'");

                    // 名称の正規表現チェック
                    if (CheckRegex(info, filesysinfo, out Match matchObj))
                    {
                        // 日時が対象日時以前かチェック
                        if (CheckTargetDt(info, filesysinfo, targetDt, timezone, matchObj))
                        {
                            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'{filesysinfo.FullName}' is target!");

                            // 圧縮対象に追加
                            compObj.Add(filesysinfo, matchObj, nowDt, timezone);

                            // 削除対象に追加
                            delObj.Add(filesysinfo);

                            // 移動対象に追加
                            moveObj.Add(filesysinfo);
                        }
                    }
                }

                // 圧縮
                compObj.Execute();
                isCleanerRunning = IsRunning();
                if (!isCleanerRunning)
                {
                    var exitmsg = $"isCleanerRunning = {isCleanerRunning} after compress.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {exitmsg}");
                    return;
                }
                // 削除
                delObj.Execute();
                isCleanerRunning = IsRunning();
                if (!isCleanerRunning)
                {
                    var exitmsg = $"isCleanerRunning = {isCleanerRunning} after delete.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {exitmsg}");
                    return;
                }

                // 移動
                moveObj.Execute();
                isCleanerRunning = IsRunning();
                if (!isCleanerRunning)
                {
                    var exitmsg = $"isCleanerRunning = {isCleanerRunning} after move.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: ExecuteFileCleaner caused by {exitmsg}");
                    return;
                }
            }
            finally
            {
                MyLogger.WriteLog(ILogger.LogLevel.INFO, $"FileCleaner Job 'info{info.InfoNumber}' End.");
                lock (info.LockObject)
                {
                    info.IsExecuting = false;
                }
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }

        /// <summary>
        /// 処理対象とする日時を取得（設定がない場合はnullを返す）
        /// </summary>
        /// <param name="info"></param>
        /// <param name="nowDt"></param>
        /// <returns></returns>
        private static DateTime? CreateTargetDt(TargetInfo info, DateTime nowDt)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            DateTime? targetDt = null;
            if (info.ElapsedTimeSetting != null)
            {
                var elapObj = info.ElapsedTimeSetting;
                targetDt = nowDt
                    .Subtract(TimeSpan.FromDays(elapObj.Day))
                    .Subtract(TimeSpan.FromHours(elapObj.Hour))
                    .Subtract(TimeSpan.FromMinutes(elapObj.Minute))
                    .Subtract(TimeSpan.FromSeconds(elapObj.Second));
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Day={elapObj.Day},Hour={elapObj.Hour},Minute={elapObj.Minute},Second={elapObj.Second},nowDt='{nowDt.ToString(Const.TIME_TOSTRING_FORMAT)}',targetDt='{((DateTime)targetDt).ToString(Const.TIME_TOSTRING_FORMAT)}'");
            }
            else
            {
                MyLogger.WriteLog(ILogger.LogLevel.DEBUG, "targetDt is null");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            return targetDt;
        }

        /// <summary>
        /// 入力ディレクトリ内のファイルorディレクトリを取得（名前でソート）
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        private static FileSystemInfo[] GetInputDirAllFiles(TargetInfo info)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            // 入力ディレクトリ
            var inputDir = new DirectoryInfo(info.InputPath);
            // 配下のファイル or ディレクトリを取得
            FileSystemInfo[] inputFileSystems = null;
            if (info.TargetType == Const.TargetType.FILE)
            {
                inputFileSystems = inputDir.GetFiles("*", info.SearchOption);
            }
            else
            {
                inputFileSystems = inputDir.GetDirectories("*", info.SearchOption);
            }
            // ファイル名 or ディレクトリ名の昇順でソート
            Array.Sort<FileSystemInfo>(inputFileSystems, delegate (FileSystemInfo a, FileSystemInfo b)
            {
                return a.FullName.CompareTo(b.FullName);
            });

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            return inputFileSystems;
        }

        /// <summary>
        /// ファイル名orディレクトリ名が正規表現にマッチするかチェック
        /// </summary>
        /// <param name="info"></param>
        /// <param name="filesysinfo"></param>
        /// <param name="matchObj"></param>
        /// <returns></returns>
        private static bool CheckRegex(TargetInfo info, FileSystemInfo filesysinfo, out Match matchObj)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            bool ret = false;
            if (!string.IsNullOrEmpty(info.RegexPattern))
            {
                Regex regObj = new Regex(info.RegexPattern);
                matchObj = regObj.Match(filesysinfo.Name);
                if (matchObj.Success)
                {
                    // 正規表現にマッチする場合、処理対象とする
                    ret = true;
                }
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"CheckRegex : regex match result is '{matchObj.Success}'. return='{ret}'");
            }
            else
            {
                // 正規表現パターンが未設定の場合、処理対象とする
                matchObj = null;
                ret = true;
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"CheckRegex : regex_pattern is not set. return='{ret}'.");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            return ret;
        }

        /// <summary>
        /// ファイルorディレクトリが処理対象日時以前かチェック
        /// </summary>
        /// <param name="info"></param>
        /// <param name="filesysinfo"></param>
        /// <param name="targetDt"></param>
        /// <param name="timezone"></param>
        /// <param name="matchObj"></param>
        /// <returns></returns>
        private static bool CheckTargetDt(TargetInfo info, FileSystemInfo filesysinfo, DateTime? targetDt, TimeZoneInfo timezone, Match matchObj)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            bool ret = false;
            if (targetDt != null && info.ElapsedTimeSetting != null)
            {
                if (info.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.UPDATE_TIME)
                {
                    var lastwritetime = TimeZoneInfo.ConvertTimeFromUtc(filesysinfo.LastWriteTimeUtc, timezone);
                    if (lastwritetime <= (DateTime)targetDt)
                    {
                        // 更新日時が処理対象日時以前の場合、処理対象とする
                        ret = true;
                    }
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"CheckTargetDt : targetDt='{((DateTime)targetDt).ToString(Const.TIME_TOSTRING_FORMAT)}', LastWriteTime='{lastwritetime.ToString(Const.TIME_TOSTRING_FORMAT)}', return='{ret}'");
                }
                else
                {
                    CultureInfo ci = CultureInfo.CurrentCulture;
                    DateTimeStyles dts = DateTimeStyles.None;
                    string dateFmt = info.ElapsedTimeSetting.DateFormat;
                    string nameDateStr = "";

                    if (info.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_PREFIX)
                    {
                        if (filesysinfo.Name.Length >= dateFmt.Length)
                        {
                            nameDateStr = filesysinfo.Name.Substring(0, dateFmt.Length);
                        }
                    }
                    else if (info.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_REGEX)
                    {
                        if (matchObj != null)
                        {
                            nameDateStr = matchObj.Groups[info.ElapsedTimeSetting.GroupName].Value;
                        }
                    }

                    if (!string.IsNullOrEmpty(nameDateStr) &&
                        DateTime.TryParseExact(nameDateStr, dateFmt, ci, dts, out DateTime outTime))
                    {
                        string targetDtStr = ((DateTime)targetDt).ToString(dateFmt);
                        if (nameDateStr.CompareTo(targetDtStr) <= 0)
                        {
                            // ファイル名の日付が処理対象日以前の場合、処理対象とする
                            ret = true;
                        }
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"CheckTargetDt : targetDtStr='{targetDtStr}', nameDateStr='{nameDateStr}', return='{ret}'");
                    }
                    else
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"CheckTargetDt : nameDateStr='{nameDateStr}' is not DateString. return='{ret}'");
                    }
                }
            }
            else
            {
                // 経過日数が未設定の場合、処理対象とする
                ret = true;
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"CheckTargetDt : elapsed_time is not set. return='{ret}'.");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            return ret;
        }

        /// <summary>
        /// ファイルをコピーする
        /// </summary>
        /// <param name="fromFileinfo">コピー元(FileInfo)</param>
        /// <param name="toPath">コピー先パス(string)</param>
        /// <param name="overwrite">上書きするかどうか</param>
        internal static void CopyFile(FileInfo fromFileinfo, string toPath, bool overwrite)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            string outputPath = Path.Combine(toPath, fromFileinfo.Name);
            fromFileinfo.CopyTo(outputPath, overwrite);
            File.SetCreationTimeUtc(outputPath, fromFileinfo.CreationTimeUtc);
            File.SetLastWriteTimeUtc(outputPath, fromFileinfo.LastWriteTimeUtc);
            File.SetLastAccessTimeUtc(outputPath, fromFileinfo.LastAccessTimeUtc);
            File.SetAttributes(outputPath, fromFileinfo.Attributes);

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }

        /// <summary>
        /// ディレクトリをサブディレクトリを含めてコピーする
        /// 例) fromDirinfo : /inputdir/test1
        ///     toPath      : /outputdir
        ///     → /outputdir/test1ディレクトリを作成し、
        ///        /inputdir/test1配下の全てのファイル、サブディレクトリを/outputdir/test1へコピーする 
        /// </summary>
        /// <param name="fromDirinfo">コピー元(DirectoryInfo)</param>
        /// <param name="toPath">コピー先パス(string)</param>
        /// <param name="overwrite">上書きするかどうか</param>
        internal static void CopyDirectory(DirectoryInfo fromDirinfo, string toPath, bool overwrite)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            // コピー先ディレクトリ存在チェック
            string outputPath = Path.Combine(toPath, fromDirinfo.Name);
            if (Directory.Exists(outputPath))
            {
                if (!overwrite)
                {
                    var errmsg = $"Directory '{outputPath}' is already exists.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CopyDirectory caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }
            else
            {
                // コピー先ディレクトリが無ければ作成
                Directory.CreateDirectory(outputPath);
                Directory.SetCreationTimeUtc(outputPath, fromDirinfo.CreationTimeUtc);
                Directory.SetLastWriteTimeUtc(outputPath, fromDirinfo.LastWriteTimeUtc);
                Directory.SetLastAccessTimeUtc(outputPath, fromDirinfo.LastAccessTimeUtc);
                new DirectoryInfo(outputPath).Attributes = fromDirinfo.Attributes;
            }

            // コピー元ディレクトリ内のファイルを全てコピー先へコピー
            foreach (var fileinfo in fromDirinfo.GetFiles())
            {
                CopyFile(fileinfo, outputPath, overwrite);
            }
            // コピー元ディレクトリ内のサブディレクトリを全てコピー先へコピー
            foreach (var dirinfo in fromDirinfo.GetDirectories())
            {
                CopyDirectory(dirinfo, outputPath, overwrite);
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }

        /// <summary>
        /// スケジューラーが動作中かつ終了処理中でないかチェック
        /// </summary>
        internal static bool IsRunning()
        {
            IApplicationEngine appEngine = ApplicationEngineFactory.GetEngine();
            bool IsTerminating = appEngine.IsTerminating();
            bool ret = IsStart && !IsTerminating;
            if (!ret)
            {
                MyLogger.WriteLog(ILogger.LogLevel.DEBUG, $"IsRunning result = {ret}, IsStart = {IsStart} ,IsTerminating = {IsTerminating}.");
            }
            return ret;
        }
    }

    internal class CompressHelper
    {
        private static ILogger MyLogger { get; } = LoggerFactory.GetLogger(typeof(CompressHelper));
        private TargetInfo Info { get; } = null;
        private Dictionary<string, List<FileSystemInfo>> CompTargetDic { get; } = new Dictionary<string, List<FileSystemInfo>>();

        internal CompressHelper(TargetInfo info)
        {
            this.Info = info;
        }

        /// <summary>
        /// 圧縮対象に追加
        /// </summary>
        /// <param name="filesysinfo"></param>
        /// <param name="matchObj"></param>
        /// <param name="nowDt"></param>
        /// <param name="timezone"></param>
        internal void Add(FileSystemInfo filesysinfo, Match matchObj, DateTime nowDt, TimeZoneInfo timezone)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            if (this.Info.Mode == Const.Mode.COMPRESS || this.Info.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                // 圧縮ファイル名を取得
                var lastwritetime = TimeZoneInfo.ConvertTimeFromUtc(filesysinfo.LastWriteTimeUtc, timezone);
                var comp_filename = this.Info.CompressFilenameInfo.CreateFilename(matchObj, lastwritetime, nowDt);
                if (string.IsNullOrEmpty(comp_filename))
                {
                    // 圧縮ファイル名の設定がされていない場合、ファイル名(拡張子無し)orディレクトリ名を圧縮ファイル名とする
                    if (Info.TargetType == Const.TargetType.FILE)
                    {
                        comp_filename = Path.GetFileNameWithoutExtension(filesysinfo.Name);
                    }
                    else
                    {
                        comp_filename = filesysinfo.Name;
                    }
                }

                // 圧縮ファイル名ごとのリストに格納
                List<FileSystemInfo> list = null;
                if (this.CompTargetDic.ContainsKey(comp_filename))
                {
                    list = this.CompTargetDic[comp_filename];
                }
                else
                {
                    list = new List<FileSystemInfo>();
                    this.CompTargetDic.Add(comp_filename, list);
                }
                list.Add(filesysinfo);
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Add : path='{filesysinfo.FullName}', comp_filename='{comp_filename}'");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }

        /// <summary>
        /// 圧縮を実行
        /// </summary>
        internal void Execute()
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : Start. target count is '{this.CompTargetDic.Count}'");

            foreach (var comp_key in this.CompTargetDic.Keys)
            {
                // スケジューラ停止または終了処理中の場合は処理を抜ける
                bool isCleanerRunning = Cleaner.IsRunning();
                if (!isCleanerRunning)
                {
                    var exitmsg = $"isCleanerRunning = {isCleanerRunning}.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: Execute caused by {exitmsg}");
                    return;
                }

                string comp_file_name = comp_key + ".zip";
                string comp_work_dir = Path.Combine(this.Info.CompressWorkPath, comp_key);
                string comp_work_file_path = Path.Combine(this.Info.CompressWorkPath, comp_file_name);
                string comp_out_file_path = Path.Combine(this.Info.OutputPath, comp_file_name);

                // 圧縮用ワークディレクトリ内に、対象ディレクトリ、対象圧縮ファイルが存在する場合は削除しておく
                if (Directory.Exists(comp_work_dir))
                {
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : work directory exists. delete now. '{comp_work_dir}'");
                    Directory.Delete(comp_work_dir, true);
                }
                if (File.Exists(comp_work_file_path))
                {
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : work compress file exists. delete now. '{comp_work_file_path}'");
                    File.Delete(comp_work_file_path);
                }

                // 出力先に既にzipファイルが存在する場合、圧縮用ワークディレクトリに解凍する
                if (File.Exists(comp_out_file_path))
                {
                    MyLogger.WriteLog(ILogger.LogLevel.DEBUG, $"'{comp_file_name}' is already exists. Extract now.");
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : extract compress file. '{comp_out_file_path}' -> '{comp_work_dir}'");
                    ZipFile.ExtractToDirectory(comp_out_file_path, comp_work_dir, true);
                }

                // ワークディレクトリを作成
                if (!Directory.Exists(comp_work_dir))
                {
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : create work directory. '{comp_work_dir}'");
                    Directory.CreateDirectory(comp_work_dir);
                }

                // 対象ファイル(orディレクトリ)をワークディレクトリにコピー
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : copy target to work directory.");
                foreach (var filesysinfo in this.CompTargetDic[comp_key])
                {
                    filesysinfo.Refresh();
                    if (filesysinfo.Exists)
                    {
                        var relativePath = filesysinfo.FullName.Substring(new DirectoryInfo(this.Info.InputPath).FullName.Length + 1);
                        if (this.Info.TargetType == Const.TargetType.FILE)
                        {
                            Cleaner.CopyFile((FileInfo)filesysinfo, comp_work_dir, true);
                            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'Info{this.Info.InfoNumber}': copy file. '{relativePath}' -> '{comp_work_dir}'");
                        }
                        else
                        {
                            Cleaner.CopyDirectory((DirectoryInfo)filesysinfo, comp_work_dir, true);
                            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'Info{this.Info.InfoNumber}': copy directory. '{relativePath}' -> '{comp_work_dir}'");
                        }
                    }
                    else
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : '{filesysinfo.FullName}' does not exist. copy skipped.");
                    }
                }

                // ワークディレクトリを圧縮
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : compress work directory. '{comp_work_dir}' -> '{comp_work_file_path}'");
                ZipFile.CreateFromDirectory(comp_work_dir, comp_work_file_path);

                // 作成された圧縮ファイルを出力先ディレクトリへコピー
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : copy compress file. '{comp_work_file_path}' -> '{this.Info.OutputPath}'");
                Cleaner.CopyFile(new FileInfo(comp_work_file_path), this.Info.OutputPath, true);

                // ワークディレクトリを削除
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : delete work directory. '{comp_work_dir}'");
                Directory.Delete(comp_work_dir, true);
                // ワーク圧縮ファイルを削除
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : delete work compress file. '{comp_work_file_path}'");
                File.Delete(comp_work_file_path);
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }
    }

    internal class DeleteHelper
    {
        private static ILogger MyLogger { get; } = LoggerFactory.GetLogger(typeof(DeleteHelper));
        private TargetInfo Info { get; } = null;
        private List<FileSystemInfo> DelTargetList { get; } = new List<FileSystemInfo>();

        internal DeleteHelper(TargetInfo info)
        {
            this.Info = info;
        }

        /// <summary>
        /// 削除対象に追加
        /// </summary>
        /// <param name="filesysinfo"></param>
        internal void Add(FileSystemInfo filesysinfo)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            if (this.Info.Mode == Const.Mode.DELETE || this.Info.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                this.DelTargetList.Add(filesysinfo);
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Add : path='{filesysinfo.FullName}'");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }

        /// <summary>
        /// 削除を実行
        /// </summary>
        internal void Execute()
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : Start. target count is '{this.DelTargetList.Count}'");

            foreach (var filesysinfo in this.DelTargetList)
            {
                // スケジューラ停止または終了処理中の場合は処理を抜ける
                bool isCleanerRunning = Cleaner.IsRunning();
                if (!isCleanerRunning)
                {
                    var exitmsg = $"isCleanerRunning = {isCleanerRunning}.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: Execute caused by {exitmsg}");
                    return;
                }

                filesysinfo.Refresh();
                if (filesysinfo.Exists)
                {
                    var relativePath = filesysinfo.FullName.Substring(new DirectoryInfo(this.Info.InputPath).FullName.Length + 1);
                    if (this.Info.TargetType == Const.TargetType.FILE)
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'Info{this.Info.InfoNumber}': delete file. '{relativePath}'");
                        filesysinfo.Delete();
                    }
                    else
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'Info{this.Info.InfoNumber}': delete drectory. '{relativePath}'");
                        ((DirectoryInfo)filesysinfo).Delete(true);
                    }
                }
                else
                {
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : '{filesysinfo.FullName}' does not exist. delete skipped.");
                }
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }
    }

    internal class MoveHelper
    {
        private static ILogger MyLogger { get; } = LoggerFactory.GetLogger(typeof(MoveHelper));
        private TargetInfo Info { get; } = null;
        private List<FileSystemInfo> MoveTargetList { get; } = new List<FileSystemInfo>();

        internal MoveHelper(TargetInfo info)
        {
            this.Info = info;
        }

        /// <summary>
        /// 移動対象に追加
        /// </summary>
        /// <param name="filesysinfo"></param>
        internal void Add(FileSystemInfo filesysinfo)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");

            if (this.Info.Mode == Const.Mode.MOVE)
            {
                this.MoveTargetList.Add(filesysinfo);
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Add : path='{filesysinfo.FullName}'");
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }

        /// <summary>
        /// 移動を実行
        /// </summary>
        internal void Execute()
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : Start. target count is '{this.MoveTargetList.Count}'");

            foreach (var filesysinfo in this.MoveTargetList)
            {
                // スケジューラ停止または終了処理中の場合は処理を抜ける
                bool isCleanerRunning = Cleaner.IsRunning();
                if (!isCleanerRunning)
                {
                    var exitmsg = $"isCleanerRunning = {isCleanerRunning}.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: Execute caused by {exitmsg}");
                    return;
                }

                filesysinfo.Refresh();
                if (filesysinfo.Exists)
                {
                    var relativePath = filesysinfo.FullName.Substring(new DirectoryInfo(this.Info.InputPath).FullName.Length + 1);
                    if (this.Info.TargetType == Const.TargetType.FILE)
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'Info{this.Info.InfoNumber}': move file. '{relativePath}' -> '{this.Info.OutputPath}'");
                        Cleaner.CopyFile((FileInfo)filesysinfo, this.Info.OutputPath, this.Info.MoveOverwrite);
                        filesysinfo.Delete();
                    }
                    else
                    {
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"'Info{this.Info.InfoNumber}': move directory. '{relativePath}' -> '{this.Info.OutputPath}'");
                        Cleaner.CopyDirectory((DirectoryInfo)filesysinfo, this.Info.OutputPath, this.Info.MoveOverwrite);
                        ((DirectoryInfo)filesysinfo).Delete(true);
                    }
                }
                else
                {
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Execute : '{filesysinfo.FullName}' does not exist. move skipped.");
                }
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: {System.Reflection.MethodBase.GetCurrentMethod().Name}");
        }
    }
}

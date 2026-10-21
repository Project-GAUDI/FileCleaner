using Newtonsoft.Json.Linq;
using System;
using TICO.GAUDI.Commons;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IotedgeV2FileCleaner
{
    // <summary>
    // desiredProperties情報格納
    // </summary>
    internal class TargetInfo
    {
        private static ILogger MyLogger { get; } = LoggerFactory.GetLogger(typeof(TargetInfo));

        public int InfoNumber { get; private set; } = 0;
        public object LockObject { get; } = new object();
        public bool IsExecuting { get; set; } = false;

        //
        // スケジュール情報
        //
        public string JobName { get; private set; } = "";
        public string TimeZone { get; private set; } = "";
        public string Second { get; private set; } = "";  // 0 - 59 or *
        public string Minute { get; private set; } = "";  // 0 - 59 or *
        public string Hour { get; private set; } = "";  // 0 - 23 or *
        public string Day { get; private set; } = "";  // 1 - 31 or *
        public string Month { get; private set; } = ""; // 1 - 12 or *
        public string Week { get; private set; } = "";  // 0 - 7 (Sunday = 0 or 7) or [sun, mon, tue, wed, thu, fri, sat] or *

        //
        // モード・対象タイプ・オプション情報
        //
        public Const.Mode Mode { get; private set; } = Const.Mode.COMPRESS;
        public Const.TargetType TargetType { get; private set; } = Const.TargetType.FILE;
        public SearchOption SearchOption { get; private set; } = SearchOption.TopDirectoryOnly;
        public bool MoveOverwrite { get; private set; } = true;

        //
        // パス情報
        //
        public string InputPath { get; private set; } = "";
        public string OutputPath { get; private set; } = "";
        public string CompressWorkPath { get; private set; } = "";

        //
        // 抽出パターン情報
        //
        public string RegexPattern { get; private set; } = "";
        public ElapsedTimeInfo ElapsedTimeSetting { get; private set; } = null;

        //
        // 圧縮ファイル情報
        //
        public CompressFileInfo CompressFilenameInfo { get; private set; } = null;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="info_number"></param>
        private TargetInfo(int info_number)
        {
            this.InfoNumber = info_number;
        }

        /// <summary>
        /// Desiredプロパティ設定値を読み込んでインスタンスを生成する
        /// </summary>
        /// <param name="jobj"></param>
        /// <param name="info_number"></param>
        /// <returns></returns>
        public static TargetInfo CreateInstance(JObject jobj, int info_number)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: CreateInstance");

            var ret = new TargetInfo(info_number);

            //
            // スケジュール情報
            //
            ret.JobName = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_JOB_NAME, false, string.Empty);
            ret.TimeZone = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_TIMEZONE, false, "UTC");
            ret.Second = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_SECOND, true);
            ret.Minute = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_MINUTE, true);
            ret.Hour = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_HOUR, true);
            ret.Day = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_DAY, true);
            ret.Month = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_MONTH, true);
            ret.Week = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_WEEK, true);

            //
            // モード・対象タイプ・オプション情報
            //
            ret.Mode = Util.GetDesiredProperty<Const.Mode>(jobj, Const.DP_KEY_MODE, true);
            ret.TargetType = Util.GetDesiredProperty<Const.TargetType>(jobj, Const.DP_KEY_TARGETTYPE, true);
            ret.SearchOption = Util.GetDesiredProperty<SearchOption>(jobj, Const.DP_KEY_SCH_OPTION, false, SearchOption.TopDirectoryOnly);
            if (ret.Mode == Const.Mode.MOVE)
            {
                ret.MoveOverwrite = Util.GetDesiredProperty<bool>(jobj, Const.DP_KEY_MOVE_OPTION, false, true);
            }

            //
            // パス情報
            //
            ret.InputPath = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_INPUT_PATH, true);

            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE || ret.Mode == Const.Mode.MOVE)
            {
                ret.OutputPath = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_OUTPUT_PATH, true);

                if (ret.Mode == Const.Mode.MOVE && string.Equals(ret.OutputPath, ret.InputPath, StringComparison.OrdinalIgnoreCase))
                {
                    var errmsg = $"Property '{Const.DP_KEY_INPUT_PATH}' and '{Const.DP_KEY_OUTPUT_PATH}' are same value.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }

            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                ret.CompressWorkPath = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_COMP_WORK_PATH, true);
                
                if (string.Equals(ret.CompressWorkPath, ret.InputPath, StringComparison.OrdinalIgnoreCase))
                {
                    var errmsg = $"Property '{Const.DP_KEY_INPUT_PATH}' and '{Const.DP_KEY_COMP_WORK_PATH}' are same value.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
                if (string.Equals(ret.CompressWorkPath, ret.OutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    var errmsg = $"Property '{Const.DP_KEY_OUTPUT_PATH}' and '{Const.DP_KEY_COMP_WORK_PATH}' are same value.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }

            //
            // 抽出パターン情報
            //
            ret.RegexPattern = Util.GetDesiredProperty<string>(jobj, Const.DP_KEY_REGEX_PATTERN, false, string.Empty);

            JObject elapsedObj = Util.GetDesiredProperty<JObject>(jobj, Const.DP_KEY_ELAPSED_SETTING, false, null);

            if (elapsedObj != null)
            {
                ret.ElapsedTimeSetting = new ElapsedTimeInfo();
                ret.ElapsedTimeSetting.JudgeType = Util.GetDesiredProperty<Const.ElapsedJudgeType>(elapsedObj, Const.DP_KEY_ELAP_JUDGE_TYPE, true);
                ret.ElapsedTimeSetting.GroupName = Util.GetDesiredProperty<string>(elapsedObj, Const.DP_KEY_ELAP_GRP_NAME, false, string.Empty);
                ret.ElapsedTimeSetting.DateFormat = Util.GetDesiredProperty<string>(elapsedObj, Const.DP_KEY_ELAP_DATE_FORMAT, false);
                if (string.IsNullOrEmpty(ret.ElapsedTimeSetting.DateFormat))
                {
                    if (ret.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_PREFIX || ret.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_REGEX)
                    {
                        ret.ElapsedTimeSetting.DateFormat = Const.DEFAULT_PREFIX_DATE_FORMAT;
                    }
                }
                ret.ElapsedTimeSetting.Day = Util.GetDesiredProperty<int>(elapsedObj, Const.DP_KEY_ELAP_DAY, false, 0, condition: x => x >= 0);
                ret.ElapsedTimeSetting.Hour = Util.GetDesiredProperty<int>(elapsedObj, Const.DP_KEY_ELAP_HOUR, false, 0, condition: x => x >= 0);
                ret.ElapsedTimeSetting.Minute = Util.GetDesiredProperty<int>(elapsedObj, Const.DP_KEY_ELAP_MINUTE, false, 0, condition: x => x >= 0);
                ret.ElapsedTimeSetting.Second = Util.GetDesiredProperty<int>(elapsedObj, Const.DP_KEY_ELAP_SECOND, false, 0, condition: x => x >= 0);
            }

            //
            // 圧縮ファイル情報
            //
            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                ret.CompressFilenameInfo = new CompressFileInfo();

                JObject compObj = Util.GetDesiredProperty<JObject>(jobj, Const.DP_KEY_COMP_FILE, false, null);

                if (compObj != null)
                {
                    ret.CompressFilenameInfo.FilenameBase = Util.GetDesiredProperty<string>(compObj, Const.DP_KEY_COMP_FILENM_BASE, true);

                    for (int i = 1; compObj.TryGetValue(Const.DP_KEY_COMP_FILENM_REPLACE_PARAM + i.ToString("D"), out JToken token); i++)
                    {
                        var paramObj = (JObject)token;

                        var param = new CompressFilenameReplaceParam();
                        ret.CompressFilenameInfo.ReplaceParamList.Add(param);

                        param.BaseName = Util.GetDesiredProperty<string>(paramObj, Const.DP_KEY_COMP_FILENM_REPLACE_BASE_NAME, true);
                        param.ReplaceType = Util.GetDesiredProperty<Const.ReplaceType>(paramObj, Const.DP_KEY_COMP_FILENM_REPLACE_TYPE, true);
                        param.ReplaceValue = Util.GetDesiredProperty<string>(paramObj, Const.DP_KEY_COMP_FILENM_REPLACE_VALUE, true);
                    }
                }
            }

            // ディレクトリ存在チェック(inputpath)
            if (!Directory.Exists(ret.InputPath))
            {
                var errmsg = $"{Const.DP_KEY_INPUT_PATH} [{ret.InputPath}] does not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }
            // ディレクトリ存在チェック(outputpath)
            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE || ret.Mode == Const.Mode.MOVE)
            {
                if (!Directory.Exists(ret.OutputPath))
                {
                    var errmsg = $"{Const.DP_KEY_OUTPUT_PATH} [{ret.OutputPath}] does not exist.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }
            // ディレクトリ存在チェック(compworkpath)
            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                if (!Directory.Exists(ret.CompressWorkPath))
                {
                    var errmsg = $"{Const.DP_KEY_COMP_WORK_PATH} [{ret.CompressWorkPath}] does not exist.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }
            // パス情報の包含関係チェック
            if (ret.TargetType == Const.TargetType.DIRECTORY)
            {
                if (!string.IsNullOrEmpty(ret.OutputPath))
                {
                    if (CheckDirectoriesRelated(ret.InputPath, ret.OutputPath))
                    {
                        var errmsg = $"input_path [{ret.InputPath}] and output_path [{ret.OutputPath}] are related.";
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                        throw new Exception(errmsg);
                    }
                }
            }
            if (ret.TargetType == Const.TargetType.FILE && ret.SearchOption == SearchOption.AllDirectories)
            {
                if (!string.IsNullOrEmpty(ret.OutputPath))
                {
                    if (CheckDirectoriesRelated(ret.InputPath, ret.OutputPath))
                    {
                        var errmsg = $"input_path [{ret.InputPath}] and output_path [{ret.OutputPath}] are related.";
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                        throw new Exception(errmsg);
                    }
                }
            }
            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                if (!string.IsNullOrEmpty(ret.CompressWorkPath))
                {
                    if (CheckDirectoriesRelated(ret.InputPath, ret.CompressWorkPath))
                    {
                        var errmsg = $"input_path [{ret.InputPath}] and comp_workpath [{ret.CompressWorkPath}] are related.";
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                        throw new Exception(errmsg);
                    }
                    else if (CheckDirectoriesRelated(ret.OutputPath, ret.CompressWorkPath))
                    {
                        var errmsg = $"output_path [{ret.OutputPath}] and comp_workpath [{ret.CompressWorkPath}] are related.";
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                        throw new Exception(errmsg);
                    }
                }
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: CreateInstance");

            return ret;
        }

        /// <summary>
        /// 2つのディレクトリの親子関係をチェック。(末尾に"/"をつけて比較)
        /// </summary>
        /// <param name="dir1"></param>
        /// <param name="dir2"></param>
        /// <returns></returns>
        public static bool CheckDirectoriesRelated(string dir1, string dir2)
        {
            static string NormalizeDirectoryPath(string path)
            {
                string fullPath = Path.GetFullPath(path);
                return Path.EndsInDirectorySeparator(fullPath)
                    ? fullPath
                    : fullPath + Path.DirectorySeparatorChar;
            }

            string absoluteDir1 = NormalizeDirectoryPath(dir1);
            string absoluteDir2 = NormalizeDirectoryPath(dir2);

            static bool IsSubdirectory(string parent, string child)
            {
                return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
            }

            return IsSubdirectory(absoluteDir1, absoluteDir2) || IsSubdirectory(absoluteDir2, absoluteDir1);
        }

        /// <summary>
        /// DesiredPropertieyから内部に保持した値を文字列で返す  ※デバッグログ出力用
        /// </summary>
        /// <returns></returns>
        public string ToString(string indent = "")
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(indent + Const.DP_KEY_JOB_NAME + " : " + this.JobName + "\n");
            sb.Append(indent + Const.DP_KEY_TIMEZONE + " : " + this.TimeZone + "\n");
            sb.Append(indent + Const.DP_KEY_SECOND + " : " + this.Second + "\n");
            sb.Append(indent + Const.DP_KEY_MINUTE + " : " + this.Minute + "\n");
            sb.Append(indent + Const.DP_KEY_HOUR + " : " + this.Hour + "\n");
            sb.Append(indent + Const.DP_KEY_DAY + " : " + this.Day + "\n");
            sb.Append(indent + Const.DP_KEY_MONTH + " : " + this.Month + "\n");
            sb.Append(indent + Const.DP_KEY_WEEK + " : " + this.Week + "\n");

            sb.Append(indent + Const.DP_KEY_MODE + " : " + this.Mode + "\n");
            sb.Append(indent + Const.DP_KEY_TARGETTYPE + " : " + this.TargetType + "\n");
            sb.Append(indent + Const.DP_KEY_SCH_OPTION + " : " + this.SearchOption + "\n");
            if (this.Mode == Const.Mode.MOVE)
            {
                sb.Append(indent + Const.DP_KEY_MOVE_OPTION + " : " + this.MoveOverwrite + "\n");
            }

            sb.Append(indent + Const.DP_KEY_INPUT_PATH + " : " + this.InputPath + "\n");
            if (this.Mode == Const.Mode.COMPRESS || this.Mode == Const.Mode.COMPRESS_AND_DELETE || this.Mode == Const.Mode.MOVE)
            {
                sb.Append(indent + Const.DP_KEY_OUTPUT_PATH + " : " + this.OutputPath + "\n");
            }
            if (this.Mode == Const.Mode.COMPRESS || this.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                sb.Append(indent + Const.DP_KEY_COMP_WORK_PATH + " : " + this.CompressWorkPath + "\n");
            }

            sb.Append(indent + Const.DP_KEY_REGEX_PATTERN + " : " + this.RegexPattern + "\n");
            sb.Append(indent + Const.DP_KEY_ELAPSED_SETTING + " :\n");
            if (this.ElapsedTimeSetting != null)
            {
                sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_JUDGE_TYPE + " : " + this.ElapsedTimeSetting.JudgeType + "\n");
                if (this.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_REGEX)
                {
                    sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_GRP_NAME + " : " + this.ElapsedTimeSetting.GroupName + "\n");
                }
                if (this.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_PREFIX || this.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_REGEX)
                {
                    sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_DATE_FORMAT + " : " + this.ElapsedTimeSetting.DateFormat + "\n");
                }
                sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_DAY + " : " + this.ElapsedTimeSetting.Day + "\n");
                sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_HOUR + " : " + this.ElapsedTimeSetting.Hour + "\n");
                sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_MINUTE + " : " + this.ElapsedTimeSetting.Minute + "\n");
                sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_ELAP_SECOND + " : " + this.ElapsedTimeSetting.Second + "\n");
            }

            if (this.Mode == Const.Mode.COMPRESS || this.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                sb.Append(indent + Const.DP_KEY_COMP_FILE + " :\n");
                sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_COMP_FILENM_BASE + " : " + this.CompressFilenameInfo.FilenameBase + "\n");
                for ( int i=0; i<this.CompressFilenameInfo.ReplaceParamList.Count; i++)
                {
                    var param = this.CompressFilenameInfo.ReplaceParamList[i];
                    sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_COMP_FILENM_REPLACE_PARAM + (i + 1).ToString("D") + " :\n");
                    sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_COMP_FILENM_REPLACE_BASE_NAME + " : " + param.BaseName + "\n");
                    sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_COMP_FILENM_REPLACE_TYPE + " : " + param.ReplaceType + "\n");
                    sb.Append(indent + Const.DESIRED_DEBUG_INDENT + Const.DESIRED_DEBUG_INDENT + Const.DP_KEY_COMP_FILENM_REPLACE_VALUE + " : " + param.ReplaceValue + "\n");
                }
            }

            return sb.ToString();
        }
    }

    internal class ElapsedTimeInfo
    {
        public Const.ElapsedJudgeType JudgeType { get; set; } = Const.ElapsedJudgeType.UPDATE_TIME;
        public string GroupName { get; set; } = "";
        public string DateFormat { get; set; } = "";
        public int Day { get; set; } = 0;
        public int Hour { get; set; } = 0;
        public int Minute { get; set; } = 0;
        public int Second { get; set; } = 0;
    }

    internal class CompressFileInfo
    {
        private static ILogger MyLogger { get; } = LoggerFactory.GetLogger(typeof(CompressFileInfo));

        public string FilenameBase { get; set; } = "";
        public List<CompressFilenameReplaceParam> ReplaceParamList { get; } = new List<CompressFilenameReplaceParam>();

        /// <summary>
        /// 圧縮先ファイル名を生成して返す
        /// </summary>
        /// <param name="matchObj"></param>
        /// <param name="fileDt"></param>
        /// <param name="nowDt"></param>
        /// <returns></returns>
        public string CreateFilename(Match matchObj, DateTime fileDt, DateTime nowDt)
        {
            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Start Method: CreateFilename");

            string ret = this.FilenameBase;
            foreach (var replaceObj in this.ReplaceParamList)
            {
                string replaceVal = replaceObj.BaseName;
                switch (replaceObj.ReplaceType)
                {
                    case Const.ReplaceType.GROUP_NAME:
                        if (matchObj != null)
                        {
                            replaceVal = matchObj.Groups[replaceObj.ReplaceValue].Value;
                        }
                        break;
                    case Const.ReplaceType.FILE_DATE:
                        replaceVal = fileDt.ToString(replaceObj.ReplaceValue);
                        break;
                    case Const.ReplaceType.NOW_DATE:
                        replaceVal = nowDt.ToString(replaceObj.ReplaceValue);
                        break;
                }
                ret = ret.Replace(replaceObj.BaseName, replaceVal);
            }

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: CreateFilename");

            return ret;
        }
    }

    internal class CompressFilenameReplaceParam
    {
        public string BaseName { get; set; } = "";
        public Const.ReplaceType ReplaceType { get; set; } = Const.ReplaceType.NOW_DATE;
        public string ReplaceValue { get; set; } = "";
    }
}

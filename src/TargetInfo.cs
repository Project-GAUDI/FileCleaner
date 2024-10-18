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

            JToken token1;
            var ret = new TargetInfo(info_number);

            //
            // スケジュール情報
            //
            if (jobj.TryGetValue(Const.DP_KEY_JOB_NAME, out token1))
            {
                ret.JobName = token1.Value<string>();
            }

            if (jobj.TryGetValue(Const.DP_KEY_TIMEZONE, out token1))
            {
                ret.TimeZone = token1.Value<string>();
            }

            ret.Second = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_SECOND);
            if (string.IsNullOrEmpty(ret.Second))
            {
                var errmsg = $"Property '{Const.DP_KEY_SECOND}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            ret.Minute = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_MINUTE);
            if (string.IsNullOrEmpty(ret.Minute))
            {
                var errmsg = $"Property '{Const.DP_KEY_MINUTE}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            ret.Hour = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_HOUR);
            if (string.IsNullOrEmpty(ret.Hour))
            {
                var errmsg = $"Property '{Const.DP_KEY_HOUR}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            ret.Day = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_DAY);
            if (string.IsNullOrEmpty(ret.Day))
            {
                var errmsg = $"Property '{Const.DP_KEY_DAY}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            ret.Month = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_MONTH);
            if (string.IsNullOrEmpty(ret.Month))
            {
                var errmsg = $"Property '{Const.DP_KEY_MONTH}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            ret.Week = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_WEEK);
            if (string.IsNullOrEmpty(ret.Week))
            {
                var errmsg = $"Property '{Const.DP_KEY_WEEK}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            //
            // モード・対象タイプ・オプション情報
            //
            var modestr = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_MODE).ToUpper();
            var modeMatch = false;
            foreach (Const.Mode value in Enum.GetValues(typeof(Const.Mode)))
            {
                if (Enum.GetName(typeof(Const.Mode), value).Equals(modestr))
                {
                    ret.Mode = value;
                    modeMatch = true;
                    break;
                }
            }
            if (!modeMatch)
            {
                var errmsg = $"Property '{Const.DP_KEY_MODE}' is not expected string.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            var targetstr = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_TARGETTYPE).ToUpper();
            var targetMatch = false;
            foreach (Const.TargetType value in Enum.GetValues(typeof(Const.TargetType)))
            {
                if (Enum.GetName(typeof(Const.TargetType), value).Equals(targetstr))
                {
                    ret.TargetType = value;
                    targetMatch = true;
                    break;
                }
            }
            if (!targetMatch)
            {
                var errmsg = $"Property '{Const.DP_KEY_TARGETTYPE}' is not expected string.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            if (jobj.TryGetValue(Const.DP_KEY_SCH_OPTION, out token1))
            {
                var schoptstr = token1.Value<string>();
                var schoptMatch = false;
                foreach (SearchOption value in Enum.GetValues(typeof(SearchOption)))
                {
                    if (Enum.GetName(typeof(SearchOption), value).Equals(schoptstr))
                    {
                        ret.SearchOption = value;
                        schoptMatch = true;
                        break;
                    }
                }
                if (!schoptMatch)
                {
                    var errmsg = $"Property '{Const.DP_KEY_SCH_OPTION}' is not expected string.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }

            if (ret.Mode == Const.Mode.MOVE)
            {
                if (jobj.TryGetValue(Const.DP_KEY_MOVE_OPTION, out token1))
                {
                    ret.MoveOverwrite = token1.Value<bool>();
                }
            }

            //
            // パス情報
            //
            ret.InputPath = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_INPUT_PATH);
            if (string.IsNullOrEmpty(ret.InputPath))
            {
                var errmsg = $"Property '{Const.DP_KEY_INPUT_PATH}' dose not exist.";
                MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                throw new Exception(errmsg);
            }

            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE || ret.Mode == Const.Mode.MOVE)
            {
                ret.OutputPath = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_OUTPUT_PATH);
                if (string.IsNullOrEmpty(ret.OutputPath))
                {
                    var errmsg = $"Property '{Const.DP_KEY_OUTPUT_PATH}' dose not exist.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
                if (ret.Mode == Const.Mode.MOVE && ret.OutputPath.Equals(ret.InputPath))
                {
                    var errmsg = $"Property '{Const.DP_KEY_INPUT_PATH}' and '{Const.DP_KEY_OUTPUT_PATH}' are same value.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }

            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                ret.CompressWorkPath = Util.GetRequiredValue<string>(jobj, Const.DP_KEY_COMP_WORK_PATH);
                if (string.IsNullOrEmpty(ret.CompressWorkPath))
                {
                    var errmsg = $"Property '{Const.DP_KEY_COMP_WORK_PATH}' dose not exist.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
                if (ret.CompressWorkPath.Equals(ret.InputPath))
                {
                    var errmsg = $"Property '{Const.DP_KEY_INPUT_PATH}' and '{Const.DP_KEY_COMP_WORK_PATH}' are same value.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
                if (ret.CompressWorkPath.Equals(ret.OutputPath))
                {
                    var errmsg = $"Property '{Const.DP_KEY_OUTPUT_PATH}' and '{Const.DP_KEY_COMP_WORK_PATH}' are same value.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }
            }

            //
            // 抽出パターン情報
            //
            if (jobj.TryGetValue(Const.DP_KEY_REGEX_PATTERN, out token1))
            {
                ret.RegexPattern = token1.Value<string>();
            }

            if (jobj.TryGetValue(Const.DP_KEY_ELAPSED_SETTING, out token1))
            {
                JObject elapsedObj = (JObject)token1;
                ret.ElapsedTimeSetting = new ElapsedTimeInfo();

                var judgestr = Util.GetRequiredValue<string>(elapsedObj, Const.DP_KEY_ELAP_JUDGE_TYPE).ToUpper();
                var judgeMatch = false;
                foreach (Const.ElapsedJudgeType value in Enum.GetValues(typeof(Const.ElapsedJudgeType)))
                {
                    if (Enum.GetName(typeof(Const.ElapsedJudgeType), value).Equals(judgestr))
                    {
                        ret.ElapsedTimeSetting.JudgeType = value;
                        judgeMatch = true;
                        break;
                    }
                }
                if (!judgeMatch)
                {
                    var errmsg = $"Property '{Const.DP_KEY_ELAP_JUDGE_TYPE}' is not expected string.";
                    MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                    throw new Exception(errmsg);
                }

                if (elapsedObj.TryGetValue(Const.DP_KEY_ELAP_GRP_NAME, out token1))
                {
                    ret.ElapsedTimeSetting.GroupName = token1.Value<string>();
                }

                if (elapsedObj.TryGetValue(Const.DP_KEY_ELAP_DATE_FORMAT, out token1))
                {
                    ret.ElapsedTimeSetting.DateFormat = token1.Value<string>();
                }
                if (string.IsNullOrEmpty(ret.ElapsedTimeSetting.DateFormat))
                {
                    if (ret.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_PREFIX
                        || ret.ElapsedTimeSetting.JudgeType == Const.ElapsedJudgeType.NAME_REGEX)
                    {
                        ret.ElapsedTimeSetting.DateFormat = Const.DEFAULT_PREFIX_DATE_FORMAT;
                    }
                }

                if (elapsedObj.TryGetValue(Const.DP_KEY_ELAP_DAY, out token1))
                {
                    ret.ElapsedTimeSetting.Day = token1.Value<int>();
                }

                if (elapsedObj.TryGetValue(Const.DP_KEY_ELAP_HOUR, out token1))
                {
                    ret.ElapsedTimeSetting.Hour = token1.Value<int>();
                }

                if (elapsedObj.TryGetValue(Const.DP_KEY_ELAP_MINUTE, out token1))
                {
                    ret.ElapsedTimeSetting.Minute = token1.Value<int>();
                }

                if (elapsedObj.TryGetValue(Const.DP_KEY_ELAP_SECOND, out token1))
                {
                    ret.ElapsedTimeSetting.Second = token1.Value<int>();
                }
            }

            //
            // 圧縮ファイル情報
            //
            if (ret.Mode == Const.Mode.COMPRESS || ret.Mode == Const.Mode.COMPRESS_AND_DELETE)
            {
                ret.CompressFilenameInfo = new CompressFileInfo();

                if (jobj.TryGetValue(Const.DP_KEY_COMP_FILE, out token1))
                {
                    var compObj = (JObject)token1;

                    ret.CompressFilenameInfo.FilenameBase = Util.GetRequiredValue<string>(compObj, Const.DP_KEY_COMP_FILENM_BASE);
                    if (string.IsNullOrEmpty(ret.CompressFilenameInfo.FilenameBase))
                    {
                        var errmsg = $"Property '{Const.DP_KEY_COMP_FILENM_BASE}' dose not exist.";
                        MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                        throw new Exception(errmsg);
                    }

                    for (int i = 1; compObj.TryGetValue(Const.DP_KEY_COMP_FILENM_REPLACE_PARAM + i.ToString("D"), out JToken token); i++)
                    {
                        var paramObj = (JObject)token;

                        var param = new CompressFilenameReplaceParam();
                        ret.CompressFilenameInfo.ReplaceParamList.Add(param);

                        param.BaseName = Util.GetRequiredValue<string>(paramObj, Const.DP_KEY_COMP_FILENM_REPLACE_BASE_NAME);
                        if (string.IsNullOrEmpty(param.BaseName))
                        {
                            var errmsg = $"Property '{Const.DP_KEY_COMP_FILENM_REPLACE_BASE_NAME}' dose not exist.";
                            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                            throw new Exception(errmsg);
                        }

                        var replacetypestr = Util.GetRequiredValue<string>(paramObj, Const.DP_KEY_COMP_FILENM_REPLACE_TYPE).ToUpper();
                        var replacetypeMatch = false;
                        foreach (Const.ReplaceType value in Enum.GetValues(typeof(Const.ReplaceType)))
                        {
                            if (Enum.GetName(typeof(Const.ReplaceType), value).Equals(replacetypestr))
                            {
                                param.ReplaceType = value;
                                replacetypeMatch = true;
                                break;
                            }
                        }
                        if (!replacetypeMatch)
                        {
                            var errmsg = $"Property '{Const.DP_KEY_COMP_FILENM_REPLACE_TYPE}' is not expected string.";
                            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                            throw new Exception(errmsg);
                        }

                        param.ReplaceValue = Util.GetRequiredValue<string>(paramObj, Const.DP_KEY_COMP_FILENM_REPLACE_VALUE);
                        if (string.IsNullOrEmpty(param.ReplaceValue))
                        {
                            var errmsg = $"Property '{Const.DP_KEY_COMP_FILENM_REPLACE_VALUE}' dose not exist.";
                            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"Exit Method: CreateInstance caused by {errmsg}");
                            throw new Exception(errmsg);
                        }
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

            MyLogger.WriteLog(ILogger.LogLevel.TRACE, $"End Method: CreateInstance");

            return ret;
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

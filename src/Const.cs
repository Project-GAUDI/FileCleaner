namespace FileCleaner
{
    internal class Const
    {
        public const string ENV_KEY_TRANSPORTPROTOCOL = "TransportProtocol";
        public const string ENV_KEY_LOGLEVEL = "LogLevel";

        public const string DP_KEY_INFO = "info";
        public const string DP_KEY_JOB_NAME = "job_name";
        public const string DP_KEY_TIMEZONE = "timezone";
        public const string DP_KEY_SECOND = "second";
        public const string DP_KEY_MINUTE = "minute";
        public const string DP_KEY_HOUR = "hour";
        public const string DP_KEY_DAY = "day";
        public const string DP_KEY_MONTH = "month";
        public const string DP_KEY_WEEK = "week";

        public const string DP_KEY_MODE = "mode";
        public const string DP_KEY_TARGETTYPE = "target_type";
        public const string DP_KEY_SCH_OPTION = "search_option";
        public const string DP_KEY_MOVE_OPTION = "move_overwrite";

        public const string DP_KEY_INPUT_PATH = "input_path";
        public const string DP_KEY_OUTPUT_PATH = "output_path";
        public const string DP_KEY_COMP_WORK_PATH = "comp_workpath";

        public const string DP_KEY_REGEX_PATTERN = "regex_pattern";
        public const string DP_KEY_ELAPSED_SETTING = "elapsed_time";
        public const string DP_KEY_ELAP_JUDGE_TYPE = "judge_type";
        public const string DP_KEY_ELAP_GRP_NAME = "group_name";
        public const string DP_KEY_ELAP_DATE_FORMAT = "date_format";
        public const string DP_KEY_ELAP_DAY = "day";
        public const string DP_KEY_ELAP_HOUR = "hour";
        public const string DP_KEY_ELAP_MINUTE = "minute";
        public const string DP_KEY_ELAP_SECOND = "second";

        public const string DP_KEY_COMP_FILE = "compress_file";
        public const string DP_KEY_COMP_FILENM_BASE = "filename";
        public const string DP_KEY_COMP_FILENM_REPLACE_PARAM = "replace_param";
        public const string DP_KEY_COMP_FILENM_REPLACE_BASE_NAME = "base_name";
        public const string DP_KEY_COMP_FILENM_REPLACE_TYPE = "replace_type";
        public const string DP_KEY_COMP_FILENM_REPLACE_VALUE = "replace_value";

        public const string DEFAULT_PREFIX_DATE_FORMAT = "yyyyMMdd";

        public const string DESIRED_DEBUG_INDENT = "  ";
        public const string TIME_TOSTRING_FORMAT = "yyyy/MM/dd HH:mm:ss";

        public enum Mode
        {
            DELETE,
            COMPRESS,
            COMPRESS_AND_DELETE,
            MOVE
        }

        public enum TargetType
        {
            FILE,
            DIRECTORY
        }

        public enum ElapsedJudgeType
        {
            UPDATE_TIME,
            NAME_PREFIX,
            NAME_REGEX
        }

        public enum ReplaceType
        {
            GROUP_NAME,
            FILE_DATE,
            NOW_DATE
        }
    }
}

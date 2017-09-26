using System;
using System.Collections.Generic;

namespace ChatServer.AnalyzeSeqnum
{
    public static class TbUtil{

        /// <summary>
        /// 通过时间获取字母表示的所在月份及第几天
        /// </summary>
        /// <param name="gtm"></param>
        /// <returns></returns>
        public static String GetDayOfMonth(DateTime gtm )
        {
            int daysInMonth = gtm.Day;
            
            return MonthToLetter(gtm.Month) + daysInMonth;
        }
     

        /// <summary>
        /// 将月份转化为字母编号(轨迹信息)
        /// </summary>
        /// <param name="month"></param>
        /// <returns></returns>
        private static String MonthToLetter(int month)
        {
            String[] letters = { "a", "b", "c", "d" };

            return letters[(month - 1) % 4];
        }

        /// <summary>
        /// 获取数据库和表序号（轨迹、报警）
        /// </summary>
        /// <param name="seqNo"></param>  序列号    
        /// <param name="rate"></param>   每张表存的最大设备数
        /// <param name="mul"></param>    序列号位数标识表示  
        /// <param name="sub"></param>    序列号减去第一位之后的字符长度
        /// <returns></returns>
        private static Dictionary<String,int> GetDbNoAndTbNo(int seqNo,int rate,int mul,int sub)
        {
            Dictionary<String, int> dic = new Dictionary<string, int>();
            int dbNo = 1, tbNo = 1;
            String seqStr = seqNo.ToString();
            dbNo = int.Parse(seqStr.Substring(0, seqStr.Length - sub));//数据库序号
            int indexOfTb = seqNo - dbNo * mul;
            tbNo = (indexOfTb - 1) / rate + 1;//数据库序号
            dic.Add("dbNo", dbNo);
            dic.Add("tbNo", tbNo);
            return dic;
        }

        /// <summary>
        /// 获取数据库和表序号（轨迹、报警）
        /// </summary>
        /// <param name="seqNo"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static Dictionary<String, int> GetDbNoAndTbNo(int seqNo,string type)
        {
            Dictionary<String, int> dic = GetRateParams(type);
            return GetDbNoAndTbNo(seqNo,dic["rate"],dic["mul"],dic["sub"]);
        }

        /// <summary>
        /// 获取数据库名
        /// </summary>
        /// <param name="dbPrex"></param> 数据库名前缀
        /// <param name="seqNo"></param>  序列号
        /// <param name="type"></param>   信息类型(轨迹，报警)
        /// <returns></returns>
        public static string GetDbName(string dbPrex,int seqNo,string type)
        {
            Dictionary<String, int> dic = GetDbNoAndTbNo(seqNo,type);
            return dbPrex + "_" + dic["dbNo"];
        }

        /// <summary>
        /// 获取表名
        /// </summary>
        /// <param name="time"></param> 时间
        /// <param name="tbPrex"></param> 表前缀
        /// <param name="seqNo"></param> 序列号
        /// <param name="type"></param> 信息类型(轨迹，报警)
        /// <returns></returns>
        public static string GetTableName(DateTime time,String tbPrex,int seqNo,string type)
        {
            string letterOfMonth = GetDayOfMonth(time);
            if (type.Equals("alarm"))
            {
                letterOfMonth = MonthToLetter(time.Month);
            }
            Dictionary<String, int> dic = GetDbNoAndTbNo(seqNo,type);
            return tbPrex + "_" + letterOfMonth + "_" + dic["tbNo"];
        }
        
        /// <summary>
        /// 判断序号是否在规则范围之内（轨迹、报警）
        /// </summary>
        /// <param name="seqNo"></param>
        /// <param name="range"></param>
        /// <param name="mul"></param>
        /// <param name="sub"></param>
        /// <returns></returns>
        private static Boolean IsSeqNoInScope(int seqNo,int range, int mul,int sub)
        {
            String seqStr = seqNo.ToString();
            int firstNum = int.Parse(seqStr.Substring(0, seqStr.Length - sub));
            int indexOfTb = seqNo - firstNum * mul;
            if ((indexOfTb + 1) <= range)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 创建新的序列号（轨迹、报警）
        /// </summary>
        /// <param name="seqNo"></param>
        /// <param name="range"></param>
        /// <param name="mul"></param>
        /// <param name="sub"></param>
        /// <returns></returns>
        private static int CreateLatestSeqNo(int seqNo, int range, int mul, int sub)
        {
            Boolean flag = IsSeqNoInScope(seqNo, range, mul, sub);
            if (flag)
            {
                return seqNo + 1;
            }
            else
            {
                String seqStr = seqNo.ToString();
                int firstNum = int.Parse(seqStr.Substring(0, seqStr.Length - sub));
                return (firstNum + 1)*mul + 1;
            }
        }

        /// <summary>
        /// 创建新的序列号（轨迹、报警）
        /// </summary>
        /// <param name="seqNo"></param>
        /// <param name="type"></param> 类型：gps：轨迹；alarm：报警
        /// <returns></returns>
        public static int CreateLatestSeqNo(int seqNo, string type)
        {
            int range = 100000, mul = 1000000, sub = 6;
            switch (type)
            {
                case "alarm":
                    range = 3000000; mul = 10000000; sub = 7;
                    break;
            }
            return CreateLatestSeqNo(seqNo,range,mul,sub);
        }

        /// <summary>
        /// 根据类型获取不同参数
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static Dictionary<String, int> GetRateParams(string type)
        {
            Dictionary<String, int> dic = new Dictionary<string, int>();
            dic.Add("rate", 10000);
            dic.Add("mul", 1000000);
            dic.Add("sub", 6);
            switch (type)
            {
                case "alarm":
                    dic["rate"] = 20000;
                    dic["mul"] = 10000000;
                    dic["sub"] = 7;
                    break;

            }
            return dic;
        }

        /// <summary>
        /// 获取时间戳
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public static long GetGMTInMS(DateTime time)
        {
            var unixTime = time.ToUniversalTime() -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return ((long)unixTime.TotalMilliseconds)/1000;
        }

    }
}

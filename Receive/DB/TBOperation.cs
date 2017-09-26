using System;
using System.Data;
using MySql.Data.MySqlClient;//导入用MySql的包
namespace Receive.DB
{
    class TBOperation
    {
        public int SelectLatterSeqNo(MySqlConnection conn, string type)
        {
            String maxSeqSql = "SELECT MAX(trail_seq_no) as seqNo,MAX(alarm_seq_no) AS alarmNo from gps_main.t_gps_main";
            DataSet seqData = null;
            try
            {
                seqData = DBGuid.Select(conn, maxSeqSql, "t_gps_main");
            }
            catch (Exception){}    
                
            int latterSeq = 1000000;
            if (type.Equals("alarm"))
            {
                latterSeq = 10000000;
            }
            if (seqData != null && seqData.Tables.Count > 0 && seqData.Tables[0].Rows.Count > 0)
            {
                DataRow seqRow = seqData.Tables[0].Rows[0];
                if (seqRow["seqNo"].ToString() != "")
                {
                    latterSeq = int.Parse(seqRow["seqNo"].ToString());
                }
                if (type.Equals("alarm"))
                {
                    if (seqRow["alarmNo"].ToString() != "")
                    {
                        latterSeq = int.Parse(seqRow["alarmNo"].ToString());
                    }

                }
            }
            return latterSeq;
        }

    }
}

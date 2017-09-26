using ChatServer.Codec;
using ChatServer.Model;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using ChatServer.AnalyzeSeqnum;
using MySql.Data.MySqlClient;//导入用MySql的包
using System.Data;
using Receive.DB;
using DB.ObjectPool;
using Common;
using System.Web.Configuration;
using System.Configuration;
using System.Collections.Specialized;
using System.Diagnostics;
namespace Receive
{
    class Program
    {
        private static Log logger = new Log("logs/Info" + DateTime.Now.ToLongDateString());
        static void Main(string[] args)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var mqSec = ConfigurationManager.GetSection("MqSection") as NameValueCollection;
            string queueName = mqSec["Queue"];//消息队列名
            ushort prefetchCount = ushort.Parse(mqSec["PrefetchCount"]);

            var taskSeq = ConfigurationManager.GetSection("TaskSection") as NameValueCollection;
            int taskNo = int.Parse(taskSeq["TaskNo"]);
            using (var connection = GetRabbitMqConnection())
            using (var channel = connection.CreateModel())
            {
                stopWatch.Stop();
                long ms = stopWatch.ElapsedMilliseconds;
                logger.log("======connected to mq server cost " + ms + "ms=====");

                channel.QueueDeclare(queue: queueName,//指定发送消息的queue，和生产者的queue匹配
                                     durable: true,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

                channel.BasicQos(prefetchSize: 0, prefetchCount: prefetchCount, global: false);

                Console.WriteLine(" [*] Waiting for messages.");

                

                //注册接收事件，一旦创建连接就去拉取消息

                for (int i = 0;i < taskNo; i++)
                {
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (model, ea) =>
                    {
                        var body = ea.Body;

                        //处理消息
                        bool isProcessed = ProcessMessage(body);
                        if (isProcessed)
                        {
                            //发送反馈，确认已处理该条消息
                            channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                        }
                    };

                    channel.BasicConsume(queue: queueName,
                                         noAck: false,//和tcp协议的ack一样，为false则服务端必须在收到客户端的回执（ack）后才能删除本条消息
                                         consumer: consumer);
                    Console.WriteLine("======consumer "+i+" created=====");
                }
                
                Console.WriteLine(" Press [enter] to exit.");
                Console.ReadLine();
            }
        }

        private static bool ProcessMessage(byte[] body)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            try
            {
                MsgDecoder msgDecoder = new MsgDecoder();

                JT808_PackageData packageData = new JT808_PackageData();

                string content = "";
                content = BitConverter.ToString(body).Replace("-", " ");

                Console.WriteLine("Received: {0}", content);
                logger.log("<<Info>>Received: " + content);

                //终端设备号 消息体前6个byte
                String deviceCode = body[0].ToString("X2") + body[1].ToString("X2") +
                                    body[2].ToString("X2") + body[3].ToString("X2") +
                                    body[4].ToString("X2") + body[5].ToString("X2");

                packageData.locationInfo = (msgDecoder.ToLocationInfoMsg(body));
                string alc = packageData.locationInfo.alc;//报警标志
                string bst = packageData.locationInfo.bst;//状态
                double lon = packageData.locationInfo.lon;//经度
                double lat = packageData.locationInfo.lat;//纬度
                double hgt = packageData.locationInfo.hgt;//高度
                double spd = packageData.locationInfo.spd;//速度
                double agl = packageData.locationInfo.agl;//方向
                DateTime gtm = packageData.locationInfo.gtm;//时间
                double mlg = packageData.locationInfo.mlg;//里程
                double oil = packageData.locationInfo.oil;//油量
                double spd2 = packageData.locationInfo.spd2;//记录仪速度
                int est = 0;//信号状态
                if (packageData.locationInfo.est != "")
                {
                    est = int.Parse(packageData.locationInfo.est);
                }
                int io = 0;//IO状态位
                if (packageData.locationInfo.io != "")
                {
                    io = int.Parse(packageData.locationInfo.io);
                }
                int ad1 = 0;//模拟量
                if (packageData.locationInfo.ad1 != "")
                {
                    ad1 = int.Parse(packageData.locationInfo.ad1);
                }
                int yte = packageData.locationInfo.yte;//信号强度
                int gnss = packageData.locationInfo.gnss;//定位卫星数

                DBConnectionSingletion pool = DBConnectionSingletion.Instance;//获取连接池对象实例
                                                                              //从配置文件读取连接字符串
                string connectionStr =
                        WebConfigurationManager.ConnectionStrings["connStr1"].ConnectionString;

                //string connectionStr = "data source='" + datasource + "';user id='" + uname + "';password='" + pwd + "';charset=utf8";
                DBConnectionSingletion.ConnectionString = connectionStr;
                MySqlConnection conn = pool.BorrowDBConnection();//从连接池借一个连接对象

                String sql = "SELECT * FROM gps_main.t_gps_main where device_code = '" + deviceCode + "'";
                DataSet result = null;
                try
                {
                    result = DBGuid.Select(conn, sql, "t_gps_main");
                }
                catch (Exception e)
                {
                    logger.log("<<Error>>" + e.Message);
                }

                //将连接对象还给连接池
                pool.ReturnDBConnection(conn);
                TBOperation operation = new TBOperation();
                int latterSeq = operation.SelectLatterSeqNo(conn, "gps");//已存在的最大轨迹序列号
                int latestSeq = TbUtil.CreateLatestSeqNo(latterSeq, "gps");//创建新的轨迹序列号

                //int latterAlarmSeq = operation.SelectLatterSeqNo(conn, "alarm");//已存在的最大报警序列号
                //int latestAlarmSeq = TbUtil.CreateLatestSeqNo(latterAlarmSeq, "alarm");//创建新的报警序列号

                if (result != null && result.Tables.Count > 0 && result.Tables[0].Rows.Count > 0)
                {//主表已保存该设备信息
                 //更新轨迹快照表sql
                    DataRow deviceRow = result.Tables[0].Rows[0];
                    string vendor = deviceRow["vendor_code"].ToString();
                    string updateSnapSql = string.Format("UPDATE gps_main.t_gps_snapshot " +
                    "set alarm_status={1},vehicle_status={2},lat={3},lon={4},height={5},speed={6}," +
                    "direction={7},time={8},mile={9},oil={10},speed2={11},signal_status={12},bst={13},io_status={14},analog={15},wifi={16},satellite_num={17},create_time={18},vendor_code='{19}'"
                    + " where device_code='{0}';",
                    deviceCode, alc, bst, lat, lon, hgt, spd, agl, TbUtil.GetGMTInMS(gtm), mlg, oil, spd2, est, bst, io, ad1, yte, gnss, "FROM_UNIXTIME(NOW())", vendor);
                    Console.WriteLine("<<Info>>update gps_main.t_gps_snapshot {0}", deviceCode);
                    logger.log("<<Info>>update gps_main.t_gps_snapshot " + deviceCode);
                    if (conn != null && !conn.State.Equals("open"))
                    {
                        conn = pool.BorrowDBConnection();//从连接池借一个连接对象
                    }

                    DBGuid.Update(conn, updateSnapSql);
                    pool.ReturnDBConnection(conn);//将连接对象还给连接池

                    //轨迹表新增一条轨迹信息
                    string dbName = TbUtil.GetDbName("gps", latestSeq, "gps");
                    string tbName = TbUtil.GetTableName(packageData.locationInfo.gtm, "t_gps", latestSeq, "gps");
                    string tbPath = dbName + "." + tbName;
                    string insertGpsSql = string.Format("INSERT INTO " + tbPath +
                        " (device_code,alarm_status,vehicle_status,lat,lon,height,speed," +
                        "direction,time,mile,oil,speed2,signal_status,bst,io_status,analog,wifi,satellite_num,create_time,vendor_code)" +
                        "VALUES ('{0}',{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},'{15}',{16},{17},{18},'{19}');",
                        deviceCode, alc, bst, lat, lon, hgt, spd, agl, TbUtil.GetGMTInMS(gtm), mlg, oil, spd2, est, bst, io, ad1, yte, gnss, "FROM_UNIXTIME(NOW())", vendor);
                    Console.WriteLine("<<Info>>insert {0} {1}", tbPath, deviceCode);
                    logger.log("<<Info>>insert " + tbPath + " " + deviceCode);

                    if (conn != null && !conn.State.Equals("open"))
                    {
                        conn = pool.BorrowDBConnection();//从连接池借一个连接对象
                    }
                    DBGuid.Insert(conn, insertGpsSql);
                    pool.ReturnDBConnection(conn);//将连接对象还给连接池

                    //报警表新增一条报警信息
                    //先判断是否有报警
                    //string alarmStatus = packageData.locationInfo.alc;
                    //if (alarmStatus.Equals("0000"))//有报警则新增
                    //{
                    //    string alarmDb = TbUtil.GetDbName("gps_alarm", latestAlarmSeq, "alarm");
                    //    string alarmTb = TbUtil.GetTableName(gtm, "t_gps_alarm", latestAlarmSeq, "alarm");
                    //    string alarmTbPath = alarmDb + "." + alarmTb;
                    //    string insertalarmsql = string.Format("insert into " + alarmTbPath +
                    //        " (device_code,alarm_status,vehicle_status,lat,lon,height,speed," +
                    //        "direction,time,mile,oil,speed2,signal_status,bst,io_status,analog,wifi,satellite_num,create_time,vendor_code,alarm_handle)" +
                    //        "values ('{0}',{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},'{15}',{16},{17},{18},'{19}',{20});",
                    //        deviceCode, alc, bst, lat, lon, hgt, spd, agl, TbUtil.GetGMTInMS(gtm), mlg, oil, spd2, est, bst, io, ad1, yte, gnss, "from_unixtime(now())", vendor, 0);

                    //}
                }
                else
                {//主表不存在该设备信息
                 //插入main表sql
                 //for test
                 //随机生成一个车牌号
                    Random R = new Random();
                    string[] strArr = {"A","B","C","D","E","F","G","H","I","J","K",
                            "L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z" };
                    int ron = R.Next(1000, 9999);
                    int ran = R.Next(0, 25);
                    String plateNo = "浙" + strArr[ran] + strArr[ran] + ron;
                    //随机生成一个运营商代码
                    String vendor = "";
                    for (int i = 0; i < 6; i++)
                    {
                        vendor += strArr[ran];
                    }
                    vendor = vendor + ron;

                    String remark = "test" + DateTime.Now.ToString();

                    //主表新增一条设备信息
                    String insertMainSql = string.Format("INSERT INTO gps_main.t_gps_main (device_code,plate_no,vendor_code,trail_seq_no,alarm_seq_no,status,remark)" +
                            "VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}');", deviceCode, plateNo, vendor, latestSeq, 0, 10, remark);
                    Console.WriteLine("<<Info>>insert gps_main.t_gps_main {0}", deviceCode);
                    logger.log("<<Info>>insert gps_main.t_gps_main " + deviceCode);

                    if (conn != null && !conn.State.Equals("open"))
                    {
                        conn = pool.BorrowDBConnection();//从连接池借一个连接对象
                    }
                    DBGuid.Insert(conn, insertMainSql);
                    pool.ReturnDBConnection(conn);//将连接对象还给连接池

                    //插入轨迹快照表sql
                    string insertSnapSql = string.Format("INSERT INTO " + "gps_main.t_gps_snapshot" +
                    " (device_code,alarm_status,vehicle_status,lat,lon,height,speed," +
                    "direction,time,mile,oil,speed2,signal_status,bst,io_status,analog,wifi,satellite_num,create_time,vendor_code)" +
                    "VALUES ('{0}',{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},'{15}',{16},{17},{18},'{19}');",
                    deviceCode, alc, bst, lat, lon, hgt, spd, agl, TbUtil.GetGMTInMS(gtm), mlg, oil, spd2, est, bst, io, ad1, yte, gnss, "FROM_UNIXTIME(NOW())", vendor);
                    if (conn != null && !conn.State.Equals("open"))
                    {
                        conn = pool.BorrowDBConnection();//从连接池借一个连接对象
                    }
                    Console.WriteLine("<<Info>>insert gps_main.t_gps_snapshot {0}", deviceCode);
                    logger.log("<<Info>>insert gps_main.t_gps_snapshot " + deviceCode);

                    DBGuid.Insert(conn, insertSnapSql);
                    pool.ReturnDBConnection(conn);//将连接对象还给连接池

                    //轨迹表新增一条轨迹信息
                    string dbName = TbUtil.GetDbName("gps", latestSeq, "gps");
                    string tbName = TbUtil.GetTableName(packageData.locationInfo.gtm, "t_gps", latestSeq, "gps");
                    string tbPath = dbName + "." + tbName;
                    string insertGpsSql = string.Format("INSERT INTO " + tbPath +
                        " (device_code,alarm_status,vehicle_status,lat,lon,height,speed," +
                        "direction,time,mile,oil,speed2,signal_status,bst,io_status,analog,wifi,satellite_num,create_time,vendor_code)" +
                        "VALUES ('{0}',{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},'{15}',{16},{17},{18},'{19}');",
                        deviceCode, alc, bst, lat, lon, hgt, spd, agl, TbUtil.GetGMTInMS(gtm), mlg, oil, spd2, est, bst, io, ad1, yte, gnss, "FROM_UNIXTIME(NOW())", vendor);
                    if (conn != null && !conn.State.Equals("open"))
                    {
                        conn = pool.BorrowDBConnection();//从连接池借一个连接对象
                    }

                    Console.WriteLine("<<Info>>insert {0} {1}", tbPath, deviceCode);
                    logger.log("<<Info>>insert " + tbPath + " " + deviceCode);
                    DBGuid.Insert(conn, insertGpsSql);
                    pool.ReturnDBConnection(conn);//将连接对象还给连接池

                    //报警表新增一条报警信息
                    //string alarmDb = TbUtil.GetDbName("gps_alarm", latestAlarmSeq, "alarm");
                    //string alarmTb = TbUtil.GetTableName(gtm, "t_gps_alarm", latestAlarmSeq, "alarm");
                    //string alarmTbPath = alarmDb + "." + alarmTb;
                    //string insertalarmsql = string.Format("insert into " + alarmTbPath +
                    //    " (device_code,alarm_status,vehicle_status,lat,lon,height,speed," +
                    //    "direction,time,mile,oil,speed2,signal_status,bst,io_status,analog,wifi,satellite_num,create_time,vendor_code,alarm_handle)" +
                    //    "values ('{0}',{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},'{15}',{16},{17},{18},'{19}',{20});",
                    //    devicecode, alc, bst, lat, lon, hgt, spd, agl, operation.getgmtinms(gtm), mlg, oil, spd2, est, bst, io, ad1, yte, gnss, "from_unixtime(now())", vendor, 0);
                    
                }
                stopWatch.Stop();
                long span = stopWatch.ElapsedMilliseconds;
                logger.log("<<<<<<<<<<<<<msg processed cost " + span + "ms >>>>>>>>>>>>>>> " + deviceCode);
            }
            catch (Exception e)
            {
                logger.log("failed to process message! " + e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 获取rabbitmq server连接
        /// </summary>
        /// <returns></returns>
        private static IConnection GetRabbitMqConnection()
        {
            try
            {
                var factory = new ConnectionFactory();

                //从App.config获取RabbiMq配置参数
                var mqSec = ConfigurationManager.GetSection("MqSection") as NameValueCollection;
                factory.HostName = mqSec["HostName"];//主机
                factory.UserName = mqSec["UserName"];//用户名
                factory.Password = mqSec["Password"];//密码
                factory.Port = int.Parse(mqSec["Port"]);//端口

                factory.AutomaticRecoveryEnabled = true;//允许自动恢复
                factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);//网络恢复间隔

                return factory.CreateConnection();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                logger.log("<<Error>>failed to create connection with rabbitmq server! " + e.Message);
                return null;
            }
        }
    }
}

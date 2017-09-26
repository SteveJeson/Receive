﻿using ChatServer.Model;
using ChatServer.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Common;
namespace ChatServer.Codec
{
    public class MsgDecoder
    {

        private static Log logger = new Log("error");

        /// <summary>
        /// 二进制数据 转成 内容包数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public JT808_PackageData Bytes2PackageData(byte[] data)
        {

            JT808_PackageData ret = new JT808_PackageData();

            // 1. 16byte 或 12byte 消息头
            JT808_PackageData.MsgHeader msgHeader = this.ParseMsgHeaderFromBytes(data);
            ret.msgHeader = msgHeader;

            int msgBodyByteStartIndex = 12 + 1;
            // 2. 消息体
            // 有子包信息,消息体起始字节后移四个字节:消息包总数(word(16))+包序号(word(16))
            if (msgHeader.hasSubPackage)
            {
                msgBodyByteStartIndex = 16 + 1;
            }

            byte[] tmp = new byte[msgHeader.msgBodyLength];
            Buffer.BlockCopy(data, msgBodyByteStartIndex, tmp, 0, tmp.Length);
            ret.setMsgBodyBytes(tmp);

            // 3. 去掉分隔符之后，最后一位就是校验码
            int checkSumInPkg = data[data.Length - 1 - 1];
            int calculatedCheckSum = ExplainUtils.getCheckSum4JT808(data, 0 + 1, data.Length - 1 - 1);
            ret.checkSum = (checkSumInPkg);
            if (checkSumInPkg != calculatedCheckSum)
            {
                ret.errorlog = ret.errorlog + "检验码不一致;";// string.Format("检验码不一致,msgid:{},pkg:{},calculated:{}", msgHeader.getMsgId(), checkSumInPkg, calculatedCheckSum);
            }
            return ret;
        }

        /// <summary>
        /// 解析生成 消息头
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private JT808_PackageData.MsgHeader ParseMsgHeaderFromBytes(byte[] bytes)
        {
            JT808_PackageData.MsgHeader msgHeader = new JT808_PackageData.MsgHeader();

            // 1. 消息ID word(16)
            msgHeader.msgId = ExplainUtils.ParseIntFromBytes(bytes, 1, 2);
            // 2. 消息体属性 word(16)
            int msgBodyProps = ExplainUtils.ParseIntFromBytes(bytes, 2 + 1, 2);
            msgHeader.msgBodyPropsField = (msgBodyProps);
            // [ 0-9 ] 0000,0011,1111,1111(3FF)(消息体长度)
            msgHeader.msgBodyLength = (msgBodyProps & 0x3ff);
            // [10-12] 0001,1100,0000,0000(1C00)(加密类型)
            msgHeader.encryptionType = ((msgBodyProps & 0x1c00) >> 10);
            // [ 13_ ] 0010,0000,0000,0000(2000)(是否有子包)
            msgHeader.hasSubPackage = (((msgBodyProps & 0x2000) >> 13) == 1);
            // [14-15] 1100,0000,0000,0000(C000)(保留位)
            msgHeader.reservedBit = (((msgBodyProps & 0xc000) >> 14) + "");
            // 3. 终端手机号 bcd[6]
            msgHeader.terminalPhone = (ExplainUtils.ParseBcdStringFromBytes(bytes, 4 + 1, 6));
            // 4. 消息流水号 word(16) 按发送顺序从 0 开始循环累加
            msgHeader.flowId = ExplainUtils.ParseIntFromBytes(bytes, 10 + 1, 2);

            // 5. 消息包封装项
            // 有子包信息
            if (msgHeader.hasSubPackage)
            {
                // 消息包封装项字段
                msgHeader.packageInfoField = ExplainUtils.ParseIntFromBytes(bytes, 12 + 1, 4);

                msgHeader.totalSubPackage = ExplainUtils.ParseIntFromBytes(bytes, 12 + 1, 2);

                msgHeader.subPackageSeq = ExplainUtils.ParseIntFromBytes(bytes, 12 + 1, 2);
            }
            return msgHeader;
        }



        /// <summary>
        /// 解析生成 位置信息 消息
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public JT808_PackageData.LocationInfo ToLocationInfoMsg(byte[] data)
        {

            JT808_PackageData.LocationInfo locationInfo = new JT808_PackageData.LocationInfo();

            //基本信息
            locationInfo.alc = data[6].ToString("X2") + data[7].ToString("X2") + data[8].ToString("X2") + data[9].ToString("X2"); //报警标志 4位
            locationInfo.bst = data[10].ToString("X2") + data[11].ToString("X2") + data[12].ToString("X2") + data[13].ToString("X2");//状态 4位
            locationInfo.lon = (double)ExplainUtils.ParseIntFromBytes(data, 14, 4) / 1000000; //经度 4位 除以10的6次方 转为度
            locationInfo.lat = (double)ExplainUtils.ParseIntFromBytes(data, 18, 4) / 1000000;//纬度 4位 除以10的6次方 转为度
            locationInfo.hgt = ExplainUtils.ParseIntFromBytes(data, 22, 2);  //高程 2位
            locationInfo.spd = (float)ExplainUtils.ParseIntFromBytes(data, 24, 2) / 10;//速度 2位 转为十进制 除以10即62.2KM/H
            locationInfo.agl = ExplainUtils.ParseIntFromBytes(data, 26, 2);//方向 2位

            string gtm = string.Format("{0}-{1}-{2} {3}:{4}:{5}", ExplainUtils.ParseBcdStringFromBytes(data, 28, 1),
                ExplainUtils.ParseBcdStringFromBytes(data, 29, 1),
                ExplainUtils.ParseBcdStringFromBytes(data, 30, 1),
                ExplainUtils.ParseBcdStringFromBytes(data, 31, 1),
                ExplainUtils.ParseBcdStringFromBytes(data, 32, 1),
                ExplainUtils.ParseBcdStringFromBytes(data, 33, 1));
            try
            {
                locationInfo.gtm = DateTime.Parse(gtm);
                //locationInfo.gtm = DateTime.Parse("09-01-2017 12:15:12");
            }
            catch (Exception e)
            {
                logger.log("error happens when parsing gtm! "+e.Message);
            }
            

            //扩展信息
            locationInfo.mlg = 0;// 里程  0x01 4
            locationInfo.oil = 0;// 油量  0x02 2
            locationInfo.spd2 = 0;// 记录仪速度 0x03 2
            locationInfo.est = "";//扩展车辆信号状态位 0x25  4
            locationInfo.io = "";// IO状态位 0x2A 2
            locationInfo.ad1 = "";// 模拟量 0x2B 4
            locationInfo.yte = 0;// 无线通信网络信号强度 0x30 1
            locationInfo.gnss = 0;// 定位卫星数 0x31 1
            locationInfo.ecu = "";// ECU透传数据 0xE0 n

            for (int i = 34; i < data.Length; i++)
            {
                if (data[i] == 0x01)// 里程  0x01 4
                {
                    locationInfo.mlg = (float)ExplainUtils.ParseIntFromBytes(data, i + 2, data[i + 1]) / 10;
                }
                else if (data[i] == 0x02)// 油量  0x02 2
                {
                    locationInfo.oil = (float)ExplainUtils.ParseIntFromBytes(data, i + 2, data[i + 1]) / 10;
                }
                else if (data[i] == 0x03)// 记录仪速度 0x03 2
                {
                    locationInfo.spd2 = ExplainUtils.ParseIntFromBytes(data, i + 2, data[i + 1]);
                }
                else if (data[i] == 0x25)//扩展车辆信号状态位 0x25  4
                {
                    for (int ii = i + 2; ii < i + 2 + data[i + 1]; ii++)
                    {
                        locationInfo.est = locationInfo.est + data[ii].ToString("X2");
                    }
                }
                else if (data[i] == 0x2a)// IO状态位 0x2A 2
                {
                    for (int ii = i + 2; ii < i + 2 + data[i + 1]; ii++)
                    {
                        locationInfo.io = locationInfo.io + data[ii].ToString("X2");
                    }
                }
                else if (data[i] == 0x2b)// 模拟量 0x2B 4
                {
                    for (int ii = i + 2; ii < i + 2 + data[i + 1]; ii++)
                    {
                        locationInfo.ad1 = locationInfo.ad1 + data[ii].ToString("X2");
                    }
                }
                else if (data[i] == 0x30)// 无线通信网络信号强度 0x30 1
                {
                    locationInfo.yte = ExplainUtils.ParseIntFromBytes(data, i + 2, data[i + 1]);
                }
                else if (data[i] == 0x31)// 定位卫星数 0x31 1
                {
                    locationInfo.gnss = ExplainUtils.ParseIntFromBytes(data, i + 2, data[i + 1]);
                }
                else if (data[i] == 0xE0)// ECU透传数据 0xE0 n
                {
                    for (int ii = i + 2; ii < i + 2 + data[i + 1]; ii++)
                    {
                        locationInfo.ecu = locationInfo.ecu + data[ii].ToString("X2");
                    }
                }
                else //其它字段，先过滤
                {

                }

                i = i + 1 + data[i + 1];
            }
            return locationInfo;
        }

        /// <summary>
        /// 处理粘包
        /// </summary>
        /// <param name="strSRecMsg"></param>
        /// <returns></returns>
        public static List<string> stickPackage(string strSRecMsg) {
            string terminateString = "7E";
            StringBuilder sb = new StringBuilder();             //这个是用来单个协议拼接
            List<string> msgList = new List<string>();          //存储有用的消息体

            string strMsg = strSRecMsg.Replace(" ", "");


            bool flag = false;
            bool bodyFlag = false;
            int rnFixLength = terminateString.Length;   //这个是指消息结束符的长度，此处为7E
            for (int i = 0; i < strMsg.Length;)         //遍历接收到的整个buffer文本
            {
                if (i <= strMsg.Length - rnFixLength)
                {
                    //两位字符
                    string strChar = strMsg.Substring(i, rnFixLength);
                    if (strChar == terminateString)//7E标示
                    {
                        if (flag == true)//找到消息尾
                        {
                            if (bodyFlag == true)//只有在有消息体的时候才组装消息
                            {
                                sb.Append(strChar);
                                flag = false;
                                bodyFlag = false;
                                msgList.Add(sb.ToString());
                                sb.Clear();
                            }

                        }
                        else //找到消息头
                        {
                            sb.Append(strChar);
                            flag = true;
                        }
                    }
                    else
                    {
                        if (flag == true)//只有找到消息头才会保留有用的消息
                        {
                            sb.Append(strChar);
                            bodyFlag = true;
                        }

                    }
                    i += rnFixLength;
                }

            }

            return msgList;
        }

    }
}

using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net;
using System.Collections;


namespace NetSyncObject
{
    ///////////////////////////////  客机 开始 ///////////////////////////////
    public class SyncClientObject: IClientBehavior
    {
        //默认数据集合
        BasicClientData basicClientData;
        //反射同步数据集合
        CustomClientData customClientData;
        //客户端状态枚举
        public enum ClientState
        {
            Join, Receiving,Start,Close,Broadcast
        }

        public SyncClientObject()
        {
            basicClientData = new BasicClientData();
            customClientData = new CustomClientData();
        }


        /// <summary>
        /// 客机行为
        /// </summary>
        /// <param name="cmd"></param>
        public void OnAction(ClientState cmd)
        {
            switch (cmd)
            {
                case ClientState.Join:
                    SendJoinRequest();
                    break;
                case ClientState.Receiving:
                    ReceiveData();
                    break;
                case ClientState.Start:
                    StartClient();
                    break;
                case ClientState.Close:
                    CloseClient();
                    break;
                case ClientState.Broadcast:
                    break;
            }
        }
        public void StartClient()
        {
            //先反射一遍
            SyncReflectionTool.Instance.Reflection(customClientData.GetType());
            //开启监听
            OnAction(ClientState.Receiving);
            //广播
            OnAction(ClientState.Broadcast);
        }
        public void CloseClient()
        {
            //关闭套接字
            if (basicClientData.receiver != null)
            {
                basicClientData.receiver.Close();
                basicClientData.receiver = null;
            }

            if (basicClientData.sender != null)
            {
                basicClientData.sender.Close();
                basicClientData.sender = null;
            }

        }
        public void SendJoinRequest()
        {
            string Ip = basicClientData.HostIP;
            int Port = basicClientData.HostPort;
            UdpClient sender = basicClientData.sender;

            //加入请求
            QAData data = new QAData(
                QAData.QuestState.ClientJoin,
                basicClientData.clientIP,
                basicClientData.ReceivePort
            );
            string jsonData = JsonSerializer.Serialize(data);
            byte[] datas = Encoding.UTF8.GetBytes(jsonData);

            //请求加入
            sender.SendAsync(datas, datas.Length, new IPEndPoint(IPAddress.Parse(Ip), Port));
        }
        public void ReceiveData()
        {
            List<Task> tasks = new List<Task>();
            tasks.Add(StartReceive());

            Task.WhenAll(tasks);
        }
        private async Task StartReceive()
        {
            //监听套接字
            UdpClient Receiver = basicClientData.receiver;

            //开始监听
            while (true)
            {
                UdpReceiveResult result;

                try
                {
                    //等待接收
                    result = await Receiver.ReceiveAsync();
                }
                catch (ObjectDisposedException) {
                    //关闭套接字
                    Receiver = null;
                    break;
                }

                //反序列化，并处理数据
                byte[] resultData = result.Buffer;
                var datas = JsonSerializer.Deserialize<NetData>(resultData);
                DataHandler.Handle(datas,this);
            }
        }
        public void Broadcast()
        {
            //序列化询问消息
            QAData quest = new QAData(
                QAData.QuestState.QueryHost, 
                basicClientData.clientIP, 
                basicClientData.ReceivePort
            );
            string jsonData = JsonSerializer.Serialize(quest);
            byte[] datas = Encoding.UTF8.GetBytes(jsonData);
            int sendPort = basicClientData.SendPort;

            //局域网广播
            UdpClient sender = new UdpClient();
            sender.EnableBroadcast = true;
            sender.SendAsync(datas, datas.Length, new IPEndPoint(IPAddress.Broadcast, sendPort));
            sender.Close();
        }


        /// <summary>
        /// 管理主机列表
        /// </summary>
        /// <param name="data"></param>
        public void HandleHostAnswerData(NetData data)
        {
            //设置主机信息
            QAData answer = data as QAData;
            if (data != null)
            {
                basicClientData.AnswerHosts.Add(Tuple.Create(answer.IP, answer.Port));
            }
        }
        public void SetHost(string IP, int Port)
        {
            basicClientData.SetHost(IP, Port);
        }
        public List<Tuple<string,int>> GetHostList()
        {
            return basicClientData.AnswerHosts;
        }
        public void ClearHostList()
        {
            basicClientData.AnswerHosts.Clear();
        }


    }


    /// <summary>
    /// 客户端行为约束
    /// </summary>
    interface IClientBehavior
    {
        //开启客户端
        void StartClient();
        //关闭客户端
        void CloseClient();
        //发送消息
        void SendJoinRequest();
        //接收消息后回调
        void ReceiveData();
        //广播
        void Broadcast();
    }



    /// <summary>
    /// 属性集合基类
    /// </summary>
    public abstract class BaseDataSet
    {
        public List<Object> GetAllReflectData()
        {
            return SyncReflectionTool.Instance.Get(GetType(), this);
        }

        public void SetAllReflectData(List<Object> Values)
        {
            SyncReflectionTool.Instance.Set(GetType(), this, Values);
        }
    }


    /// <summary>
    /// 默认数据集合
    /// </summary>
    public class BasicClientData : BaseDataSet
    {
        //本机IP
        public string clientIP;
        //主机IP
        public string HostIP;
        //客机发送端口
        public int SendPort;
        //客机接收端口
        public int ReceivePort;
        //主机接收端口
        public int HostPort;
        //客户端发送套接字
        public UdpClient sender;
        //客户端接收套接字
        public UdpClient receiver;
        //临时存主机
        public List<Tuple<string, int>> AnswerHosts;

        public BasicClientData(string clientIP, int ReceivePort,int SendPort)
        {
            this.clientIP = clientIP;
            this.ReceivePort = ReceivePort;

            IPEndPoint ipe1 = new IPEndPoint(IPAddress.Parse(clientIP), ReceivePort);
            receiver = new UdpClient(ipe1);

            IPEndPoint ipe2 = new IPEndPoint(IPAddress.Parse(clientIP), SendPort);
            sender = new UdpClient(ipe2);

            AnswerHosts = new List<Tuple<string, int>>();
        }
        public BasicClientData() { AnswerHosts = new List<Tuple<string, int>>(); }

        public void SetHost(string HostIP,int HostReceivePort)
        {
            this.HostIP = HostIP;
            this.HostPort = HostReceivePort;
        }
    }


    /// <summary>
    /// 自定义数据集合，可使用NetValueAttribute标识需要反射的字段
    /// </summary>
    public class CustomClientData : BaseDataSet
    {

    }

    ///////////////////////////////  客机 结束 ///////////////////////////////







    ///////////////////////////////  主机 开始 ///////////////////////////////
    public class SyncServer
    {

    }

    ///////////////////////////////  主机 结束 ///////////////////////////////







    ///////////////////////////////  客机池 开始 ///////////////////////////////
    public class SyncPool
    {
        static SyncPool instance;
        internal static SyncPool Instance
        {
            get {
                lock (instance)
                {
                    if (instance == null)
                    {
                        lock (instance)
                        {
                            Init();
                        }
                    }
                }
                return instance;
            }
        }

        //客机池
        static List<BasicClientData> _PoolBasic;
        static List<CustomClientData> _PoolCustom;
        static Dictionary<int, bool> _Active;
        //客机数量
        static int _ClientNum;


        static void Init()
        {
            instance = new SyncPool();
            _PoolBasic = new List<BasicClientData>();
            _PoolCustom = new List<CustomClientData>();
            _Active = new Dictionary<int, bool>();
            _ClientNum = 0;
        }

        internal int  AddClient()
        {
            int index = -1;
            for(int i=0;i< _Active.Count;i++)
            {
                if (_Active[i] == false)
                {
                    _Active[i] = true;
                    index = i;
                }
            }
            //如果没找到就添加
            if (index == -1)
            {
                _PoolBasic.Add(new BasicClientData());
                _PoolCustom.Add(new CustomClientData());
                _Active.Add(index, true);
                _ClientNum++;
            }

            return index;
        }
        internal void RemoveClient(int index)
        {
            _ClientNum--;
            _Active[index] = false;
        }
        internal Tuple<BasicClientData, CustomClientData> GetClientData(int index)
        {
            return Tuple.Create(_PoolBasic[index], _PoolCustom[index]);
        }

        internal void ResetPool()
        {
            _PoolBasic.Clear();
            _PoolCustom.Clear();
            _Active.Clear();
            _ClientNum = 0;
        }

        public static int GetClientNum()
        {
            return _ClientNum;
        }
    }
    ///////////////////////////////  客机池 结束 ///////////////////////////////







    ///////////////////////////////  网络消息 开始 ///////////////////////////////

    /// <summary>
    /// 消息基类
    /// </summary>
    [Serializable]
    public abstract class NetData
    {
        //消息请求类型
        public enum QuestState
        {
            QueryHost,HostAnswer,Close,NormalData,ClientJoin
        }
        public QuestState state;

        //以键值对(Json)方式储存
        public Dictionary<int, List<Object>> args;
    }

    /// <summary>
    /// 请求消息
    /// </summary>
    [Serializable]
    public class QAData : NetData
    {
        // 请求客机IP/回应主机IP
        public string IP;
        // 请求接收端口/回应主机端口
        public int Port;

        public QAData(QuestState state, string ip, int port)
        {
            this.state = state;
            this.IP = ip;
            this.Port = port;
        }
    }

    /// <summary>
    /// 普通同步数据
    /// </summary>
    [Serializable]
    public class NormalData : NetData
    {

    }

    /// <summary>
    /// 消息处理
    /// </summary>
    public class DataHandler
    {
        public static void Handle(NetData data, SyncClientObject client)
        {
            switch (data.state)
            {
                case NetData.QuestState.QueryHost:
                    break;
                case NetData.QuestState.HostAnswer:
                    client.HandleHostAnswerData(data);
                    break;
                case NetData.QuestState.Close:
                    break;
                case NetData.QuestState.NormalData:
                    break;
            }
        }
    }

    ///////////////////////////////  网络消息 结束 ///////////////////////////////








    ///////////////////////////////  反射工具 开始 ///////////////////////////////
    internal class SyncReflectionTool
    {
        /// <summary>
        /// 单例，仅此程序集可访问
        /// </summary>
        private static SyncReflectionTool instance;
        public static SyncReflectionTool Instance
        {
            get
            {
                lock (instance)
                {
                    if (instance == null)
                    {
                        lock (instance){
                            Init();
                        }
                    }
                    return instance;
                }
            }
        }


        /// <summary>
        /// 记录反射信息
        /// </summary>
        private static Dictionary<Type, List<FieldInfo>> _typeDic;


        /// <summary>
        /// 赋值和设值方式
        /// </summary>
        private static Dictionary<Tuple<Type, Type>, Delegate> _GetWay;
        private static Dictionary<Tuple<Type, Type>, Delegate> _SetWay;


        /// <summary>
        /// 赋值或设值枚举
        /// </summary>
        enum GetOrSet{SetWay,GetWay}


        ////////////////////////////////////////////////////////////////////////////////////////////////////


        /// <summary>
        /// 初始化
        /// </summary>
        private static void Init()
        {
            instance = new SyncReflectionTool();
            _GetWay = new Dictionary<Tuple<Type, Type>, Delegate>();
            _SetWay = new Dictionary<Tuple<Type, Type>, Delegate>();
            _typeDic = new Dictionary<Type, List<FieldInfo>>();
        }


        /// <summary>
        /// 反射类型，缓存下来信息
        /// </summary>
        /// <param name="Client"></param>
        public void Reflection(Type Client)
        {
            if (_typeDic.ContainsKey(Client))
            {
                return;
            }

            //反射查找字段
            BindingFlags bf = BindingFlags.Public| BindingFlags.Instance | BindingFlags.Static;
            var filedList = Client.GetFields(bf)
                .Where(filed=>Attribute.IsDefined(filed,typeof(NetValueAttribute))).ToList();

            //缓存反射信息
            List<FieldInfo> list = new List<FieldInfo>();
            foreach (var filedinfo in filedList)
            {
                //基于反射信息缓存委托
                AddAction(Client, filedinfo, GetOrSet.GetWay);
                AddAction(Client, filedinfo, GetOrSet.SetWay);
                list.Add(filedinfo);
            }

            _typeDic.Add(Client, list);
        }



        /// <summary>
        /// 设值与赋值
        /// </summary>
        /// <param name="ClientType"></param>
        /// <param name="client"></param>
        /// <param name="Values"></param>
        public void Set(Type ClientType, Object client, List<Object> Values)
        {
            if (!_typeDic.ContainsKey(ClientType)||!_typeDic.ContainsKey(ClientType))
            {
                return;
            }

            //获取反射信息
            List<FieldInfo> list = _typeDic[ClientType];
            int indx = 0;

            //这里Values的值顺序和list必须相同
            foreach (var field in list) {
                var setter = (Action<Object,Object>)GetActionByCMD(ClientType, field.FieldType, GetOrSet.SetWay);
                if (setter != null)
                {
                    setter(client, Values[indx]);
                }
                indx++;
            }
        }
        public List<Object> Get(Type ClientType, Object client)
        {
            if (!_typeDic.ContainsKey(ClientType))
            {
                return null;
            }

            List<FieldInfo> list = _typeDic[ClientType];
            List<Object> values = new List<Object>();
            int indx = 0;
            foreach (var field in list)
            {
                var getter = (Func<Object,Object>)GetActionByCMD(ClientType, field.FieldType, GetOrSet.GetWay);
                if (getter != null) {
                    var value = getter(client);
                    if (value != null)values.Add(value);
                }
                indx++;
            }

            return values;
        }



        /// <summary>
        /// 获取委托
        /// </summary>
        /// <param name="client"></param>
        /// <param name="value"></param>
        /// <param name="cmd"></param>
        /// <returns></returns>
        private Delegate GetActionByCMD(Type client, Type value, GetOrSet cmd)
        {
            Tuple<Type,Type>pair=Tuple.Create(client,value);
            Dictionary<Tuple<Type, Type>, Delegate> _Way = null;

            switch (cmd)
            {
                case GetOrSet.GetWay:
                    _Way = _GetWay;
                    break;
                case GetOrSet.SetWay:
                    _Way = _SetWay;
                    break;
            }

            if (_Way.ContainsKey(pair))
            {
                return _Way[pair];
            }
            else
            {
                return null;
            }
        }


        /// <summary>
        /// 缓存委托
        /// </summary>
        /// <param name="client"></param>
        /// <param name="value"></param>
        /// <param name="cmd"></param>
        private void AddAction(Type client, FieldInfo value, GetOrSet cmd)
        {
            Tuple<Type, Type> pair = Tuple.Create(client, value.FieldType);
            //参数确定
            Dictionary<Tuple<Type, Type>, Delegate> _Way = null;
            MethodInfo method = null;

            switch (cmd)
            {
                case GetOrSet.SetWay:
                    _Way = _SetWay;
                    method = CreateSetterMethodInfo(client, value);
                    break;
                case GetOrSet.GetWay:
                    _Way = _GetWay;
                    method = CreateGetterMethodInfo(client, value);
                    break;
            }

            //缓存委托
            if (!_Way.ContainsKey(pair))
            {
                var act = GetAction(client, value, method, cmd);
                _Way.Add(pair, act);
            }
        }


        /// <summary>
        /// 基于IL动态代码，获取赋值设值动态方法
        /// </summary>
        /// <param name="client"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private MethodInfo CreateSetterMethodInfo(Type client,FieldInfo value) {
            var dynamicMethod = new DynamicMethod(
                name: $"Setter_{client.DeclaringType.Name}_{value.FieldType.Name}",
                returnType: null,
                parameterTypes: new Type[2] { typeof(Object), typeof(Object) },
                restrictedSkipVisibility: true
            );

            //IL生成器
            var il = dynamicMethod.GetILGenerator();

            //加载两个参数
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);

            //赋值
            il.Emit(OpCodes.Stfld,value);

            //返回
            il.Emit(OpCodes.Ret);

            return dynamicMethod;
        }
        private MethodInfo CreateGetterMethodInfo(Type client, FieldInfo value)
        {
            var dynamicMethod = new DynamicMethod(
                name: $"Setter_{client.DeclaringType.Name}_{value.FieldType.Name}",
                returnType: typeof(Object),
                parameterTypes: new Type[2] { typeof(Object), typeof(Object) },
                restrictedSkipVisibility: true
            );

            //IL生成器
            var il = dynamicMethod.GetILGenerator();

            //加载参数
            il.Emit(OpCodes.Ldarg_0);

            //取值
            il.Emit(OpCodes.Ldfld, value);

            //返回
            il.Emit(OpCodes.Ret);

            return dynamicMethod;
        }


        /// <summary>
        /// 获取委托
        /// </summary>
        /// <param name="client"></param>
        /// <param name="value"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        private Delegate GetAction(Type client, FieldInfo value, MethodInfo method, GetOrSet cmd)
        {
            Delegate del = null;
            switch (cmd) {
                case GetOrSet.SetWay:
                    del = (Action<Object, Object>)Delegate.CreateDelegate(
                        typeof(Action<Object, Object>),
                        null,
                        method
                    );
                    break;
                case GetOrSet.GetWay:
                    del = (Func<Object, Object>)Delegate.CreateDelegate(
                        typeof(Func<Object, Object>),
                        null,
                        method
                    );
                    break;
            }
            return del;
        }
    }


    ///////////////////////////////  网络消息 结束 ///////////////////////////////








    ///////////////////////////////  自定义Attribute 开始 ///////////////////////////////

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    internal class NetValueAttribute : Attribute
    {
        /// <summary>
        /// 特性描述
        /// </summary>
        public string Desc;

        public NetValueAttribute(string desc) {
            Desc = desc;
        }
    }


    ///////////////////////////////  自定义Attribute 结束 ///////////////////////////////
}

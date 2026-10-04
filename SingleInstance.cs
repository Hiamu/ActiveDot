using System;
using System.Diagnostics;
using System.Threading;

namespace ActiveDot
{
    /// <summary>单实例互斥锁。</summary>
    public static class SingleInstance
    {
        private const string MutexName = "Global\\ActiveDot_SingleInstance";
        private static Mutex _mutex;

        /// <summary>尝试获取单实例锁。返回 false 表示已有实例在运行。</summary>
        public static bool EnsureSingleInstance()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                _mutex = null;
                return false;
            }
            // 持有互斥锁直到进程退出（不释放），防止垃圾回收
            return true;
        }
    }
}
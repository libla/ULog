# ULog
*just a log interface for unity. (detail in chinese)*

一个日志接口库

-  可以运行时打开/关闭Debug和堆栈
-  可以自定义输出函数，减少不必要的堆栈获取行为，或者把日志输出到文件、网络
-  可以多线程调用，而最终输出函数在主线程被调用。若不用于多线程也没有额外代价

___
>为什么使用单独的DLL，不放在unity工程内部？

使用封装的DLL并在内部使用Debug.Log等函数时，可以避免在编辑器中点击log会错误的跳转到日志接口内部
#pragma once

// C++ 后端的应用层入口声明。
// main.cpp 只负责适配 wmain，实际命令解析和生命周期管理在 app.cpp 中完成。

namespace miniav
{
// 执行命令行后端程序并返回进程退出码。
int RunApp(int argc, wchar_t* argv[]);
} // namespace miniav

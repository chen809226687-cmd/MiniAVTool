#include "app.h"

// Windows 宽字符入口，保证输入路径、输出路径和设备/文件名中的
// 非 ASCII 字符能够以 wchar_t 形式传给后端。
int main(int argc, wchar_t* argv[])
{
    // 将真正的程序逻辑交给 app.cpp，保持 main.cpp 只承担入口适配职责。
    return miniav::RunApp(argc, argv);
}

// 摄像头模块：负责选择后端并打开采集设备。
#include "camera/camera_capture.h"

// 截图模块：负责创建目录和写入 JPG 文件。
#include "io/snapshot_writer.h"

// UI 模块：负责绘制状态信息和时间戳。
#include "ui/overlay.h"

// 视觉模块：负责显示模式、运动检测和图像处理。
#include "vision/frame_processor.h"

// highgui 提供窗口、键盘事件和图像显示接口。
#include <opencv2/highgui.hpp>

// VideoCapture 在 main 中以 RAII 对象形式持有摄像头。
#include <opencv2/videoio.hpp>

// max 用于把用户输入的摄像头索引限制为非负数。
#include <algorithm>

// steady_clock 用于计算稳定的帧间隔和 FPS。
#include <chrono>

// stoi 可能抛出 invalid_argument 或 out_of_range。
#include <exception>

// path 用于表示 captures 输出目录。
#include <filesystem>

// 控制台输出启动信息和错误信息。
#include <iostream>

// 命令行参数和错误文本使用 string。
#include <string>

namespace {

// OpenCV 窗口的固定名称。
constexpr char kWindowName[] = "MiniAV Camera Vision";

// 截图保存目录，相对于程序当前工作目录。
constexpr auto kCaptureDirectory = "captures";

int parseCameraIndex(const int argc, char* argv[]) {
    // 没有提供参数时使用默认摄像头 0。
    if (argc < 2) {
        return 0;
    }

    try {
        // 将第一个命令行参数解析为摄像头索引。
        const auto index = std::stoi(argv[1]);

        // 防止负索引传入 OpenCV。
        return std::max(0, index);
    } catch (const std::exception&) {
        // 参数不是整数时提示用户，并回退到默认摄像头。
        std::cerr << "Invalid camera index '" << argv[1] << "', using 0.\n";
        return 0;
    }
}

void printUsage() {
    // 启动时打印命令行用法和交互按键。
    std::cout << "Usage: MiniAVTool.CameraVision [camera-index]\n"
              << "Controls: 1-5 change view, S save snapshot, ESC exit.\n";
}

} // namespace

int main(int argc, char* argv[]) {
    // 先打印使用说明，方便直接从终端启动程序的用户。
    printUsage();

    // VideoCapture 对象代表当前摄像头连接。
    cv::VideoCapture camera;

    // 由摄像头模块填充可读的失败原因。
    std::string error;

    // 根据命令行索引打开摄像头；打开失败时结束程序。
    if (!miniav::camera::open(camera, parseCameraIndex(argc, argv), error)) {
        std::cerr << error << '\n';
        return 1;
    }

    // 创建一个可调整大小的 OpenCV 窗口。
    cv::namedWindow(kWindowName, cv::WINDOW_NORMAL);

    // 设置窗口初始尺寸，不会强制改变摄像头真实分辨率。
    cv::resizeWindow(kWindowName, 1280, 720);

    // 默认显示原始彩色画面。
    auto mode = miniav::vision::ViewMode::Original;

    // 由运动检测函数持续维护的灰度背景模型。
    cv::Mat backgroundGray;

    // camera.read 每一帧写入的原始图像。
    cv::Mat frame;

    // 当前模式处理后的图像，也是显示和截图的来源。
    cv::Mat processed;

    // 截图输出目录使用 path，避免手工拼接平台相关分隔符。
    const auto captureDirectory = std::filesystem::path(kCaptureDirectory);

    // 使用单调时钟计算帧率，避免系统时间被校准时导致 FPS 跳变。
    auto previousFrameTime = std::chrono::steady_clock::now();

    // FPS 使用平滑值，减少单帧耗时变化导致的画面抖动。
    double fps = 0.0;

    // 主循环每次处理一帧并响应一次键盘输入。
    while (true) {
        // 从摄像头读取下一帧；读取失败通常表示设备断开或后端异常。
        if (!camera.read(frame) || frame.empty()) {
            std::cerr << "Failed to read a camera frame.\n";
            break;
        }

        // 记录当前帧到达时间。
        const auto currentFrameTime = std::chrono::steady_clock::now();

        // 计算两帧之间经过的秒数。
        const auto elapsed =
            std::chrono::duration<double>(currentFrameTime - previousFrameTime).count();
        previousFrameTime = currentFrameTime;
        if (elapsed > 0.0) {
            // 用当前帧间隔计算瞬时 FPS。
            const auto instantFps = 1.0 / elapsed;

            // 首帧直接使用瞬时值，后续使用 90% 历史值 + 10% 新值平滑。
            fps = fps == 0.0 ? instantFps : fps * 0.9 + instantFps * 0.1;
        }

        // 先更新运动检测背景，并得到当前帧的运动区域。
        const auto motion = miniav::vision::detectMotion(frame, backgroundGray);

        // 根据当前显示模式得到待显示图像。
        processed = miniav::vision::processFrame(frame, mode, motion);

        // 在处理结果上叠加 FPS、时间戳、当前模式和快捷键提示。
        miniav::ui::drawOverlay(processed, fps, mode,
                                miniav::ui::formatTimestamp(
                                    std::chrono::system_clock::now()),
                                motion.regions.size());

        // 把最终画面显示到窗口。
        cv::imshow(kWindowName, processed);

        // waitKey 既负责处理窗口事件，也负责读取键盘输入。
        const int key = cv::waitKey(1) & 0xFF;
        if (key == 27) {
            // ESC 键退出主循环。
            break;
        }

        switch (key) {
        case '1':
            // 原始彩色画面。
            mode = miniav::vision::ViewMode::Original;
            break;
        case '2':
            // 灰度画面。
            mode = miniav::vision::ViewMode::Gray;
            break;
        case '3':
            // Canny 边缘画面。
            mode = miniav::vision::ViewMode::Edges;
            break;
        case '4':
            // 固定阈值二值画面。
            mode = miniav::vision::ViewMode::Threshold;
            break;
        case '5':
            // 运动区域标记画面。
            mode = miniav::vision::ViewMode::Motion;
            break;
        case 's':
        case 'S': {
            // 截图使用当前已经处理并叠加信息的画面。
            std::filesystem::path savedPath;
            if (miniav::io::saveSnapshot(processed, captureDirectory, savedPath, error)) {
                std::cout << "Saved: " << savedPath.string() << '\n';
            } else {
                std::cerr << "Could not save snapshot: " << error << '\n';
            }
            break;
        }
        default:
            // 其他键不改变当前模式。
            break;
        }
    }

    // 显式释放摄像头资源。
    camera.release();

    // 关闭 OpenCV 创建的所有窗口。
    cv::destroyAllWindows();

    // 正常结束程序。
    return 0;
}

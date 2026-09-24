#include <opencv2/core.hpp>
#include <opencv2/geometry/2d.hpp>
#include <opencv2/highgui.hpp>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/videoio.hpp>

#include <algorithm>
#include <chrono>
#include <ctime>
#include <exception>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {

constexpr char kWindowName[] = "MiniAV Camera Vision";

enum class ViewMode {
    Original,
    Gray,
    Edges,
    Threshold,
    Motion,
};

struct MotionResult {
    cv::Mat mask;
    std::vector<cv::Rect> regions;
};

std::string formatTimestamp(const std::chrono::system_clock::time_point now) {
    const auto time = std::chrono::system_clock::to_time_t(now);
    std::tm localTime{};
#ifdef _WIN32
    localtime_s(&localTime, &time);
#else
    localtime_r(&time, &localTime);
#endif

    std::ostringstream output;
    output << std::put_time(&localTime, "%Y-%m-%d %H:%M:%S");
    return output.str();
}

std::string modeName(const ViewMode mode) {
    switch (mode) {
    case ViewMode::Original:
        return "Original";
    case ViewMode::Gray:
        return "Gray";
    case ViewMode::Edges:
        return "Canny edges";
    case ViewMode::Threshold:
        return "Threshold";
    case ViewMode::Motion:
        return "Motion";
    }

    return "Unknown";
}

bool openCamera(cv::VideoCapture& camera, const int cameraIndex) {
#ifdef _WIN32
    if (!camera.open(cameraIndex, cv::CAP_DSHOW)) {
        return false;
    }
#else
    if (!camera.open(cameraIndex)) {
        return false;
    }
#endif

    camera.set(cv::CAP_PROP_FRAME_WIDTH, 1280);
    camera.set(cv::CAP_PROP_FRAME_HEIGHT, 720);
    camera.set(cv::CAP_PROP_FPS, 30);
    return true;
}

MotionResult detectMotion(const cv::Mat& frame, cv::Mat& backgroundGray) {
    cv::Mat gray;
    cv::cvtColor(frame, gray, cv::COLOR_BGR2GRAY);
    cv::GaussianBlur(gray, gray, cv::Size(21, 21), 0.0);

    if (backgroundGray.empty()) {
        backgroundGray = gray.clone();
        return {};
    }

    cv::Mat difference;
    cv::absdiff(backgroundGray, gray, difference);
    cv::threshold(difference, difference, 25, 255, cv::THRESH_BINARY);
    cv::dilate(difference, difference, cv::Mat(), cv::Point(-1, -1), 2);

    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(difference, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

    MotionResult result;
    result.mask = difference;
    for (const auto& contour : contours) {
        if (cv::contourArea(contour) >= 900.0) {
            result.regions.push_back(cv::boundingRect(contour));
        }
    }

    // Slowly follow lighting changes without immediately absorbing moving objects.
    cv::addWeighted(backgroundGray, 0.95, gray, 0.05, 0.0, backgroundGray);
    return result;
}

cv::Mat processFrame(const cv::Mat& frame, const ViewMode mode, const MotionResult& motion) {
    cv::Mat gray;
    cv::Mat output;

    switch (mode) {
    case ViewMode::Original:
        output = frame.clone();
        break;
    case ViewMode::Gray:
        cv::cvtColor(frame, output, cv::COLOR_BGR2GRAY);
        cv::cvtColor(output, output, cv::COLOR_GRAY2BGR);
        break;
    case ViewMode::Edges:
        cv::cvtColor(frame, gray, cv::COLOR_BGR2GRAY);
        cv::GaussianBlur(gray, gray, cv::Size(5, 5), 0.0);
        cv::Canny(gray, gray, 50.0, 150.0);
        cv::cvtColor(gray, output, cv::COLOR_GRAY2BGR);
        break;
    case ViewMode::Threshold:
        cv::cvtColor(frame, gray, cv::COLOR_BGR2GRAY);
        cv::threshold(gray, gray, 120.0, 255.0, cv::THRESH_BINARY);
        cv::cvtColor(gray, output, cv::COLOR_GRAY2BGR);
        break;
    case ViewMode::Motion:
        output = frame.clone();
        for (const auto& region : motion.regions) {
            cv::rectangle(output, region, cv::Scalar(0, 0, 255), 2);
            cv::putText(output, "motion", region.tl() + cv::Point(0, -8),
                        cv::FONT_HERSHEY_SIMPLEX, 0.65, cv::Scalar(0, 0, 255), 2,
                        cv::LINE_AA);
        }
        break;
    }

    return output;
}

void drawOverlay(cv::Mat& frame, const double fps, const ViewMode mode,
                 const std::string& timestamp, const std::size_t motionCount) {
    const auto white = cv::Scalar(245, 245, 245);
    const auto cyan = cv::Scalar(255, 210, 0);
    const auto black = cv::Scalar(20, 20, 20);

    cv::rectangle(frame, cv::Rect(0, 0, frame.cols, 82), black, cv::FILLED);
    cv::putText(frame, "MiniAV Camera Vision", cv::Point(18, 28),
                cv::FONT_HERSHEY_SIMPLEX, 0.72, cyan, 2, cv::LINE_AA);

    std::ostringstream metrics;
    metrics << std::fixed << std::setprecision(1) << "FPS: " << fps
            << " | " << frame.cols << "x" << frame.rows
            << " | Mode: " << modeName(mode);
    cv::putText(frame, metrics.str(), cv::Point(18, 56),
                cv::FONT_HERSHEY_SIMPLEX, 0.58, white, 1, cv::LINE_AA);

    cv::putText(frame, timestamp, cv::Point(18, frame.rows - 42),
                cv::FONT_HERSHEY_SIMPLEX, 0.58, white, 1, cv::LINE_AA);
    cv::putText(frame, "1 Original  2 Gray  3 Edge  4 Binary  5 Motion  S Snapshot  ESC Exit",
                cv::Point(18, frame.rows - 16), cv::FONT_HERSHEY_SIMPLEX, 0.48, white, 1,
                cv::LINE_AA);

    if (mode == ViewMode::Motion) {
        std::ostringstream motionText;
        motionText << "Motion regions: " << motionCount;
        cv::putText(frame, motionText.str(), cv::Point(frame.cols - 220, 56),
                    cv::FONT_HERSHEY_SIMPLEX, 0.55, cv::Scalar(0, 220, 255), 1,
                    cv::LINE_AA);
    }
}

int parseCameraIndex(const int argc, char* argv[]) {
    if (argc < 2) {
        return 0;
    }

    try {
        return std::max(0, std::stoi(argv[1]));
    } catch (const std::exception&) {
        return 0;
    }
}

} // namespace

int main(int argc, char* argv[]) {
    const auto cameraIndex = parseCameraIndex(argc, argv);
    cv::VideoCapture camera;
    if (!openCamera(camera, cameraIndex)) {
        std::cerr << "Cannot open camera index " << cameraIndex << ".\n"
                  << "Make sure the camera is connected and not in use by another app.\n";
        return 1;
    }

    std::filesystem::create_directories("captures");
    cv::namedWindow(kWindowName, cv::WINDOW_NORMAL);
    cv::resizeWindow(kWindowName, 1280, 720);

    ViewMode mode = ViewMode::Original;
    cv::Mat backgroundGray;
    cv::Mat frame;
    cv::Mat processed;
    auto previousFrameTime = std::chrono::steady_clock::now();
    double fps = 0.0;

    std::cout << "Camera opened. Press 1-5 to change mode, S to save, ESC to exit.\n";

    while (true) {
        if (!camera.read(frame) || frame.empty()) {
            std::cerr << "Failed to read a camera frame.\n";
            break;
        }

        const auto currentFrameTime = std::chrono::steady_clock::now();
        const auto elapsed = std::chrono::duration<double>(currentFrameTime - previousFrameTime).count();
        previousFrameTime = currentFrameTime;
        if (elapsed > 0.0) {
            const auto instantFps = 1.0 / elapsed;
            fps = fps == 0.0 ? instantFps : fps * 0.9 + instantFps * 0.1;
        }

        const auto motion = detectMotion(frame, backgroundGray);
        processed = processFrame(frame, mode, motion);
        drawOverlay(processed, fps, mode, formatTimestamp(std::chrono::system_clock::now()),
                    motion.regions.size());
        cv::imshow(kWindowName, processed);

        const int key = cv::waitKey(1) & 0xFF;
        if (key == 27) {
            break;
        }

        switch (key) {
        case '1':
            mode = ViewMode::Original;
            break;
        case '2':
            mode = ViewMode::Gray;
            break;
        case '3':
            mode = ViewMode::Edges;
            break;
        case '4':
            mode = ViewMode::Threshold;
            break;
        case '5':
            mode = ViewMode::Motion;
            break;
        case 's':
        case 'S': {
            const auto milliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(
                std::chrono::system_clock::now().time_since_epoch()).count();
            const auto fileName = std::filesystem::path("captures") /
                ("frame_" + std::to_string(milliseconds) + ".jpg");
            if (cv::imwrite(fileName.string(), processed)) {
                std::cout << "Saved: " << fileName.string() << '\n';
            }
            break;
        }
        default:
            break;
        }
    }

    camera.release();
    cv::destroyAllWindows();
    return 0;
}

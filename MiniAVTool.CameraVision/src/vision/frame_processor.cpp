#include "frame_processor.h"

// 2D 几何接口提供 cv::Rect、轮廓面积和边界矩形相关类型/函数。
#include <opencv2/geometry/2d.hpp>

// 图像颜色转换、滤波、阈值、轮廓和边缘检测都属于 imgproc 模块。
#include <opencv2/imgproc.hpp>

namespace miniav::vision {

MotionResult detectMotion(const cv::Mat& frame, cv::Mat& backgroundGray) {
    // 运动检测不需要颜色信息，先把 BGR 图像转换为单通道灰度图。
    cv::Mat gray;
    cv::cvtColor(frame, gray, cv::COLOR_BGR2GRAY);

    // 高斯模糊可以削弱传感器噪声，减少微小噪声被识别为运动。
    cv::GaussianBlur(gray, gray, cv::Size(21, 21), 0.0);

    // 第一帧没有可比较的历史背景，因此只初始化背景模型。
    if (backgroundGray.empty()) {
        backgroundGray = gray.clone();
        return {};
    }

    // 计算当前灰度帧与背景灰度帧之间的绝对差异。
    cv::Mat difference;
    cv::absdiff(backgroundGray, gray, difference);

    // 过滤低于阈值的亮度变化，得到黑白运动掩码。
    cv::threshold(difference, difference, 25, 255, cv::THRESH_BINARY);

    // 膨胀白色区域，使相邻的小块运动区域更容易连接起来。
    cv::dilate(difference, difference, cv::Mat(), cv::Point(-1, -1), 2);

    // 从二值掩码中提取外部轮廓。
    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(difference, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_SIMPLE);

    // 只保留面积足够大的轮廓，避免把噪声当成有效运动。
    MotionResult result;
    for (const auto& contour : contours) {
        if (cv::contourArea(contour) >= 900.0) {
            // 将轮廓转换为最小外接矩形，便于后续绘制和计数。
            result.regions.push_back(cv::boundingRect(contour));
        }
    }

    // 以较慢速度更新背景模型，使它能够适应光照变化，
    // 同时避免移动物体立即被完全吸收进背景。
    cv::addWeighted(backgroundGray, 0.95, gray, 0.05, 0.0, backgroundGray);
    return result;
}

cv::Mat processFrame(const cv::Mat& frame, const ViewMode mode,
                     const MotionResult& motion) {
    // gray 保存需要单通道处理的中间结果。
    cv::Mat gray;

    // output 是最终交给界面显示和截图保存的图像。
    cv::Mat output;

    switch (mode) {
    case ViewMode::Original:
        // clone 确保输出图像拥有自己的存储空间，后续叠加文字不会修改输入帧。
        output = frame.clone();
        break;
    case ViewMode::Gray:
        // 先转换为灰度，再转换回三通道，
        // 这样所有显示模式都能使用统一的彩色画布绘制叠加信息。
        cv::cvtColor(frame, output, cv::COLOR_BGR2GRAY);
        cv::cvtColor(output, output, cv::COLOR_GRAY2BGR);
        break;
    case ViewMode::Edges:
        // Canny 通常在灰度且平滑后的图像上执行。
        cv::cvtColor(frame, gray, cv::COLOR_BGR2GRAY);
        cv::GaussianBlur(gray, gray, cv::Size(5, 5), 0.0);
        cv::Canny(gray, gray, 50.0, 150.0);
        cv::cvtColor(gray, output, cv::COLOR_GRAY2BGR);
        break;
    case ViewMode::Threshold:
        // 使用固定阈值把灰度图转换成黑白二值图。
        cv::cvtColor(frame, gray, cv::COLOR_BGR2GRAY);
        cv::threshold(gray, gray, 120.0, 255.0, cv::THRESH_BINARY);
        cv::cvtColor(gray, output, cv::COLOR_GRAY2BGR);
        break;
    case ViewMode::Motion:
        // 运动模式保留原始画面，只在检测到的区域上绘制标记。
        output = frame.clone();
        for (const auto& region : motion.regions) {
            // 红色矩形表示检测到的运动区域。
            cv::rectangle(output, region, cv::Scalar(0, 0, 255), 2);

            // 在矩形上方写出 motion 标签。
            cv::putText(output, "motion", region.tl() + cv::Point(0, -8),
                        cv::FONT_HERSHEY_SIMPLEX, 0.65, cv::Scalar(0, 0, 255), 2,
                        cv::LINE_AA);
        }
        break;
    }

    return output;
}

const char* modeName(const ViewMode mode) {
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

} // namespace miniav::vision

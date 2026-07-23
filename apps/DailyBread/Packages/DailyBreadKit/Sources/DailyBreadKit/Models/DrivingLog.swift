import Foundation

/// One supervised-driving entry. Times are "HH:mm" strings; status rides as a
/// string so an unexpected value can't fail the decode.
public struct DrivingLogEntry: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var childUserId: String
    public var childName: String
    public var date: DayDate
    public var startTime: String
    public var endTime: String
    public var durationMinutes: Int
    public var isNightDriving: Bool
    public var supervisorLabel: String
    public var weather: String
    public var routeNotes: String?
    public var createdByParent: Bool
    public var status: String
    public var createdAt: LenientDate?
    public var decidedAt: LenientDate?
    public var decidedByLabel: String?
    public var rejectionReason: String?

    public var isPending: Bool { status == "PendingApproval" }
    public var isApproved: Bool { status == "Approved" }
    public var isRejected: Bool { status == "Rejected" }

    /// "1h 20m" from the stored minutes.
    public var durationLabel: String {
        let h = durationMinutes / 60, m = durationMinutes % 60
        if h > 0 && m > 0 { return "\(h)h \(m)m" }
        return h > 0 ? "\(h)h" : "\(m)m"
    }
}

public struct DrivingLogProgress: Codable, Hashable, Sendable {
    public var totalHours: Double
    public var totalGoalHours: Double?
    public var nightHours: Double
    public var nightGoalHours: Double?
}

public struct DrivingLogCreate: Codable, Sendable {
    public var childUserId: String?
    public var date: DayDate
    public var startTime: String
    public var endTime: String
    public var nightOverride: Bool?
    public var supervisorName: String?
    public var weather: String
    public var routeNotes: String?

    public init(childUserId: String? = nil, date: DayDate, startTime: String, endTime: String,
                nightOverride: Bool? = nil, supervisorName: String? = nil,
                weather: String = "Clear", routeNotes: String? = nil) {
        self.childUserId = childUserId
        self.date = date
        self.startTime = startTime
        self.endTime = endTime
        self.nightOverride = nightOverride
        self.supervisorName = supervisorName
        self.weather = weather
        self.routeNotes = routeNotes
    }
}

public enum DrivingWeather: String, CaseIterable, Identifiable, Sendable {
    case clear = "Clear", rain = "Rain", snow = "Snow", fog = "Fog", ice = "Ice", other = "Other"
    public var id: String { rawValue }
    public var label: String { rawValue }
}

import XCTest
@testable import DailyBreadKit

/// The wire conventions the whole app depends on: money-as-string,
/// dates as yyyy-MM-dd, lenient timestamps.
final class WireTypeTests: XCTestCase {

    // MARK: - Money

    func testMoneyDecodesFromString() throws {
        let money = try JSONDecoder().decode(Money.self, from: Data("\"12.50\"".utf8))
        XCTAssertEqual(money.amount, Decimal(string: "12.50"))
    }

    func testMoneyDecodesFromNumberFallback() throws {
        let money = try JSONDecoder().decode(Money.self, from: Data("2.5".utf8))
        XCTAssertEqual(money.amount, Decimal(string: "2.5"))
    }

    func testMoneyEncodesAsTwoPlaceString() throws {
        let data = try JSONEncoder().encode(Money(Decimal(string: "2.5")!))
        XCTAssertEqual(String(data: data, encoding: .utf8), "\"2.50\"")
    }

    func testMoneySignedDisplay() {
        XCTAssertEqual(Money(Decimal(2)).signedDisplay.hasPrefix("+"), true)
        XCTAssertEqual(Money(Decimal(-5)).signedDisplay.hasPrefix("−"), true)
    }

    // MARK: - DayDate

    func testDayDateRoundTrip() throws {
        let date = try JSONDecoder().decode(DayDate.self, from: Data("\"2026-07-19\"".utf8))
        XCTAssertEqual(date, DayDate(year: 2026, month: 7, day: 19))
        let encoded = try JSONEncoder().encode(date)
        XCTAssertEqual(String(data: encoded, encoding: .utf8), "\"2026-07-19\"")
    }

    func testDayDateOrdering() {
        XCTAssertLessThan(DayDate(year: 2026, month: 7, day: 18),
                          DayDate(year: 2026, month: 7, day: 19))
        XCTAssertLessThan(DayDate(year: 2025, month: 12, day: 31),
                          DayDate(year: 2026, month: 1, day: 1))
    }

    func testDayDateRejectsGarbage() {
        XCTAssertNil(DayDate(wireString: "not-a-date"))
        XCTAssertNil(DayDate(wireString: "2026-13-01"))
    }

    // MARK: - LenientDate

    func testLenientDateParsesDotNetFractionalSeconds() {
        XCTAssertNotNil(LenientDate.parse("2026-07-19T23:12:00.1234567Z"))
    }

    func testLenientDateParsesWithoutZone() {
        XCTAssertNotNil(LenientDate.parse("2026-07-19T23:12:00"))
    }

    func testLenientDateParsesPlainUTC() {
        XCTAssertNotNil(LenientDate.parse("2026-07-19T23:12:00Z"))
    }

    // MARK: - DTO decoding smoke test

    func testTodayChoresDecodes() throws {
        let json = """
        {"date":"2026-07-19","userId":"u1","userName":"kid","items":[
          {"choreDefinitionId":1,"choreLogId":10,"name":"Dishes","description":null,
           "icon":"🍽️","earnValue":"2.50","penaltyValue":"0.00","status":"Pending",
           "scheduleType":"SpecificDays","weeklyTargetCount":0,"weeklyCompletedCount":0,
           "isRepeatable":false,"helpReason":null,"helpRequestedAtUtc":null,
           "approvedByUserName":null,"approvedAtUtc":null}]}
        """
        let today = try JSONDecoder().decode(TodayChores.self, from: Data(json.utf8))
        XCTAssertEqual(today.items.count, 1)
        XCTAssertEqual(today.items[0].earnValue.wireString, "2.50")
        XCTAssertTrue(today.items[0].isPending)
    }
}

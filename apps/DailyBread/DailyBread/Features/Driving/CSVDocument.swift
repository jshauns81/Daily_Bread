import SwiftUI
import UniformTypeIdentifiers

/// A CSV handed to the system's export sheet.
///
/// `FileDocument` buys one code path for both platforms — `.fileExporter`
/// resolves to an NSSavePanel on macOS and the Files/share sheet on iOS — so
/// the driving log doesn't need a hand-rolled save panel on one side and a
/// UIActivityViewController on the other. The bytes come from the server
/// already formatted; this type only carries them.
struct CSVDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.commaSeparatedText] }

    var data: Data

    init(data: Data) {
        self.data = data
    }

    init(configuration: ReadConfiguration) throws {
        data = configuration.file.regularFileContents ?? Data()
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: data)
    }
}

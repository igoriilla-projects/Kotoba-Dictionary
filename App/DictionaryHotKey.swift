import AppKit
import Carbon.HIToolbox
import SwiftUI

struct DictionaryKeyCombination: Equatable {
    let key: UInt32
    let modifiers: UInt32

    static let keys: [(String, UInt32)] = [
        ("A", 0), ("B", 11), ("C", 8), ("D", 2), ("E", 14), ("F", 3), ("G", 5),
        ("H", 4), ("I", 34), ("J", 38), ("K", 40), ("L", 37), ("M", 46), ("N", 45),
        ("O", 31), ("P", 35), ("Q", 12), ("R", 15), ("S", 1), ("T", 17), ("U", 32),
        ("V", 9), ("W", 13), ("X", 7), ("Y", 16), ("Z", 6),
        ("0", 29), ("1", 18), ("2", 19), ("3", 20), ("4", 21), ("5", 23), ("6", 22),
        ("7", 26), ("8", 28), ("9", 25), ("Пробел", 49),
        ("F1", 122), ("F2", 120), ("F3", 99), ("F4", 118), ("F5", 96), ("F6", 97),
        ("F7", 98), ("F8", 100), ("F9", 101), ("F10", 109), ("F11", 103), ("F12", 111),
        ("F13", 105), ("F14", 107), ("F15", 113), ("F16", 106), ("F17", 64),
        ("F18", 79), ("F19", 80), ("F20", 90)
    ]
    static let modifierKeys: [(String, UInt32)] = [
        ("Control", UInt32(controlKey)), ("Option", UInt32(optionKey)),
        ("Shift", UInt32(shiftKey)), ("Command", UInt32(cmdKey))
    ]
    static let standard = Self(key: UInt32(kVK_ANSI_J), modifiers: UInt32(controlKey | optionKey))

    var isValid: Bool {
        let allowed = UInt32(controlKey | optionKey | shiftKey | cmdKey)
        return Self.keys.contains { $0.1 == key }
            && modifiers & ~allowed == 0 && modifiers & UInt32(controlKey | cmdKey) != 0
    }

    var title: String {
        let keys = Self.modifierKeys.compactMap { title, flag in modifiers & flag == 0 ? nil : title }
        return (keys + [Self.keys.first { $0.1 == key }?.0 ?? ""]).joined(separator: "+")
    }
}

@MainActor
final class DictionaryHotKey: ObservableObject {
    static let shared = DictionaryHotKey()
    private static let preference = "dictionaryHotKey"
    @Published private(set) var selection: DictionaryKeyCombination?
    @Published private(set) var registrationError: String?
    @Published var searchPending = false
    private var showWindow: (() -> Void)?
    private var handler: EventHandlerRef?
    private var registration: EventHotKeyRef?
    private var activeID: UInt32 = 0
    private var nextID: UInt32 = 0

    private init() {
        if let stored = UserDefaults.standard.dictionary(forKey: Self.preference) {
            if stored.isEmpty {
                selection = nil
            } else if let key = stored["key"] as? UInt32, let modifiers = stored["modifiers"] as? UInt32 {
                let combination = DictionaryKeyCombination(key: key, modifiers: modifiers)
                selection = combination.isValid ? combination : .standard
            } else {
                selection = .standard
            }
        } else {
            selection = .standard
        }
    }

    func start(showWindow: @escaping () -> Void) {
        self.showWindow = showWindow
        if registration == nil, let selection {
            apply(selection, persist: false)
        }
    }

    func apply(_ combination: DictionaryKeyCombination?, persist: Bool = true) {
        if let combination, !combination.isValid {
            registrationError = "Добавьте Control или Command и выберите клавишу."
            return
        }
        if combination != selection || (combination != nil && registration == nil) {
            var replacement: EventHotKeyRef?
            if let combination {
                let handlerStatus = installHandler()
                guard handlerStatus == noErr else {
                    registrationError = "Не удалось включить глобальное сочетание (\(handlerStatus))."
                    return
                }
                nextID &+= 1
                let identifier = EventHotKeyID(signature: 0x4B544241, id: nextID)
                let status = RegisterEventHotKey(combination.key, combination.modifiers, identifier,
                                                 GetApplicationEventTarget(), 0, &replacement)
                guard status == noErr else {
                    registrationError = "Сочетание недоступно. Выберите другое (\(status))."
                    return
                }
            }
            if let registration { UnregisterEventHotKey(registration) }
            registration = replacement
            activeID = replacement == nil ? 0 : nextID
        }
        if persist {
            let value: [String: Int] = combination.map { ["key": Int($0.key), "modifiers": Int($0.modifiers)] } ?? [:]
            UserDefaults.standard.set(value, forKey: Self.preference)
            selection = combination
        }
        registrationError = nil
    }

    private func installHandler() -> OSStatus {
        if handler != nil { return noErr }
        var event = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        return InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var identifier = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &identifier)
            guard status == noErr, identifier.signature == 0x4B544241 else { return OSStatus(eventNotHandledErr) }
            let hotKey = Unmanaged<DictionaryHotKey>.fromOpaque(context).takeUnretainedValue()
            let id = identifier.id
            DispatchQueue.main.async {
                guard hotKey.activeID == id else { return }
                hotKey.searchPending = true
                hotKey.showWindow?()
            }
            return noErr
        }, 1, &event, Unmanaged.passUnretained(self).toOpaque(), &handler)
    }
}

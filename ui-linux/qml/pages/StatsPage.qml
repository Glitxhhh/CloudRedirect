import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Page {
    title: "Stats Sync"

    ScrollView {
        anchors.fill: parent
        contentWidth: availableWidth

        ColumnLayout {
            width: parent.width
            spacing: 12

            Item { height: 8 }

            Label {
                text: "Stats Sync"
                font.pointSize: 16
                font.bold: true
                Layout.leftMargin: 20
            }

            Label {
                text: "Changes apply the next time Steam starts."
                opacity: 0.7
                Layout.leftMargin: 20
                Layout.rightMargin: 20
                wrapMode: Text.WordWrap
                Layout.fillWidth: true
            }

            Frame {
                Layout.fillWidth: true
                Layout.leftMargin: 20
                Layout.rightMargin: 20

                RowLayout {
                    anchors.fill: parent
                    spacing: 8

                    ColumnLayout {
                        Layout.fillWidth: true
                        spacing: 2

                        Label {
                            text: "Sync Achievements"
                            font.bold: true
                        }
                        Label {
                            text: "blah blah wip wip"
                            opacity: 0.7
                            wrapMode: Text.WordWrap
                            Layout.fillWidth: true
                        }
                    }

                    Switch {
                        checked: backend ? backend.syncAchievements : false
                        onToggled: { if (backend) backend.syncAchievements = checked }
                    }
                }
            }

            Frame {
                Layout.fillWidth: true
                Layout.leftMargin: 20
                Layout.rightMargin: 20

                RowLayout {
                    anchors.fill: parent
                    spacing: 8

                    ColumnLayout {
                        Layout.fillWidth: true
                        spacing: 2

                        Label {
                            text: "Sync Playtime"
                            font.bold: true
                        }
                        Label {
                            text: "blah blah wip wip"
                            opacity: 0.7
                            wrapMode: Text.WordWrap
                            Layout.fillWidth: true
                        }
                    }

                    Switch {
                        checked: backend ? backend.syncPlaytime : false
                        onToggled: { if (backend) backend.syncPlaytime = checked }
                    }
                }
            }

            Item { Layout.fillHeight: true }
        }
    }
}

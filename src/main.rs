
use iced::widget::{button, column, text, Column};
use iced::Theme;

pub fn main() -> iced::Result {
    iced::application(u64::default, update, view)
        .theme(Theme::Light)
        .centered()
        .run()
}

#[derive(Debug, Clone)]
enum Message {
    Increment,
}

fn update(value: &mut u64, message: Message) {
    match message {
        Message::Increment => *value += 1,
    }
}

fn view(value: &u64) -> Column<'_, Message> {
    column![
        text(value),
        button("+").on_press(Message::Increment)
    ]
}

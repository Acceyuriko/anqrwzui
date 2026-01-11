use iced::widget::{button, column, text};
use iced::{Element, Sandbox, Settings};

fn main() -> iced::Result {
    MyApp::run(Settings::default())
}

struct MyApp {
    count: i32,
}

#[derive(Debug, Clone, Copy)]
enum Message {
    IncrementPressed,
    DecrementPressed,
}

impl Sandbox for MyApp {
    type Message = Message;

    fn new() -> Self {
        Self { count: 0 }
    }

    fn title(&self) -> String {
        String::from("Iced GUI App")
    }

    fn update(&mut self, message: Message) {
        match message {
            Message::IncrementPressed => self.count += 1,
            Message::DecrementPressed => self.count -= 1,
        }
    }

    fn view(&self) -> Element<'_, Message> {
        column![
            text(format!("Current count: {}", self.count)),
            button("Increment").on_press(Message::IncrementPressed),
            button("Decrement").on_press(Message::DecrementPressed),
        ]
        .padding(20)
        .spacing(10)
        .into()
    }
}

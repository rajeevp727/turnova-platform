import { StatusBar } from "expo-status-bar";
import { SafeAreaView, StyleSheet, Text, View } from "react-native";

export default function App() {
  return (
    <SafeAreaView style={styles.container}>
      <StatusBar style="auto" />
      <View style={styles.content}>
        <Text style={styles.brand}>TurnOva</Text>
        <Text style={styles.tagline}>Everyone’s Turn Matters.</Text>
        <Text style={styles.heading}>Driver</Text>
        <Text style={styles.body}>Your driver app foundation is ready.</Text>
        <Text style={styles.note}>Queue, availability and trip features will be added next.</Text>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#F7F8FA" },
  content: { flex: 1, justifyContent: "center", padding: 28 },
  brand: { fontSize: 32, fontWeight: "800", color: "#152B45" },
  tagline: { marginTop: 4, color: "#617184", fontSize: 14 },
  heading: { marginTop: 48, fontSize: 24, fontWeight: "700", color: "#152B45" },
  body: { marginTop: 12, fontSize: 17, color: "#26384A" },
  note: { marginTop: 8, fontSize: 14, color: "#617184" }
});
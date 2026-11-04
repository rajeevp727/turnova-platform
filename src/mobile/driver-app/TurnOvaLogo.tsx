import React from "react";
import { View, Text, StyleSheet } from "react-native";
import Svg, { Path, Circle } from "react-native-svg";

type Props = { subtitle?: string; compact?: boolean };

export default function TurnOvaLogo({ subtitle, compact = false }: Props) {
  return (
    <View style={styles.row}>
      <Svg width={compact ? 42 : 52} height={compact ? 42 : 50} viewBox="0 0 100 100">
        <Path d="M8 53 30 17Q33 12 40 12H78Q86 12 81 20L59 56Q56 61 49 61H14Q4 61 8 53Z" fill="#087BFA" />
        <Path d="M42 83 64 47Q67 42 74 42H88Q96 42 91 50L70 84Q67 89 60 89H48Q38 89 42 83Z" fill="#12DDB8" />
        <Path d="M22 35 27 27Q29 24 33 24H51Q55 24 57 28L62 35M20 36H62V45H20Z" fill="none" stroke="#FFFFFF" strokeWidth="4" strokeLinejoin="round" strokeLinecap="round" />
        <Circle cx="29" cy="46" r="3" fill="#FFFFFF" /><Circle cx="54" cy="46" r="3" fill="#FFFFFF" />
        <Circle cx="69" cy="58" r="3.5" fill="#FFFFFF" />
        <Path d="M67 64 74 69 82 69 86 74 76 74 70 70 65 79 58 79 65 69 61 66Z" fill="#FFFFFF" />
        <Path d="M61 80H87" stroke="#FFFFFF" strokeWidth="3.5" strokeLinecap="round" />
        <Circle cx="64" cy="82" r="5" fill="none" stroke="#FFFFFF" strokeWidth="3" />
        <Circle cx="85" cy="82" r="5" fill="none" stroke="#FFFFFF" strokeWidth="3" />
      </Svg>
      <View style={styles.copy}>
        <Text style={[styles.wordmark, compact && styles.compact]}><Text style={styles.turn}>Turn</Text><Text style={styles.ova}>Ova</Text></Text>
        {!!subtitle && <Text style={styles.subtitle}>{subtitle}</Text>}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: "row", alignItems: "center", gap: 10 },
  copy: { justifyContent: "center" },
  wordmark: { color: "#FFFFFF", fontSize: 26, fontWeight: "900", fontStyle: "italic", letterSpacing: -1.1 },
  compact: { fontSize: 23 },
  turn: { color: "#FFFFFF" },
  ova: { color: "#18DDB9" },
  subtitle: { color: "#B8C5D2", fontSize: 11, marginTop: 2 }
});
